using ClientiX.Domain.Entities;
using ClientiX.Infrastructure.Repositories;
using ClientiX.Infrastructure.State;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

using DomainUser = ClientiX.Domain.Entities.User;

namespace ClientiX.BotGateway;

public class TelegramPollingService : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly ILogger<TelegramPollingService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly UserStateService _states;

    public TelegramPollingService(
        ITelegramBotClient bot,
        ILogger<TelegramPollingService> logger,
        IServiceScopeFactory scopeFactory,
        UserStateService states)
    {
        _bot = bot;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _states = states;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var me = await _bot.GetMe(stoppingToken);
        _logger.LogInformation(
            "ClientiX BotGateway запущен. Главный бот: @{Username} (id {Id})",
            me.Username, me.Id);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery },
            DropPendingUpdates = true
        };

        _bot.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleUpdateAsync(
        ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            if (update.Message is { Text: not null } message)
            {
                _logger.LogInformation(
                    "Сообщение от @{Username} (id {UserId}): {Text}",
                    message.From?.Username ?? "—",
                    message.From?.Id,
                    message.Text);

                if (message.Text.StartsWith("/start"))
                {
                    await HandleStartAsync(bot, message, ct);
                }
                else
                {
                    await HandleTextMessageAsync(bot, message, ct);
                }
            }
            else if (update.CallbackQuery is { } callback)
            {
                await HandleCallbackAsync(bot, callback, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обработки апдейта {UpdateId}", update.Id);
        }
    }

    /// <summary>
    /// /start: регистрация нового мастера или возврат существующего в меню.
    /// </summary>
    private async Task HandleStartAsync(
        ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        if (message.From is null) return;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();

        var user = await users.GetByTelegramIdAsync(message.From.Id, ct);
        bool isNew = user is null;

        if (isNew)
        {
            user = await users.CreateMasterAsync(
                telegramId: message.From.Id,
                username: message.From.Username,
                firstName: message.From.FirstName,
                lastName: message.From.LastName,
                ct: ct);

            _logger.LogInformation(
                "Зарегистрирован новый мастер: id={UserId}, tg={TgId}, ref={Ref}",
                user.Id, user.TelegramId, user.ReferralCode);

            // Запускаем анкету сразу после регистрации
            await StartOnboardingAsync(bot, message.Chat.Id, user, ct);
            return;
        }

        // Существующий мастер — проверяем, прошёл ли анкету
        if (string.IsNullOrEmpty(user.ManagedBot?.Niche))
        {
            // Анкета не завершена — продолжаем
            await StartOnboardingAsync(bot, message.Chat.Id, user, ct);
            return;
        }

        // Всё заполнено — обычное меню
        await _states.ClearAsync(message.From.Id);
        await SendMainMenuAsync(bot, message.Chat.Id, user, ct);
    }

    /// <summary>
    /// Запуск анкеты (шаг 1: ниша).
    /// </summary>
    private async Task StartOnboardingAsync(
        ITelegramBotClient bot, long chatId, DomainUser user, CancellationToken ct)
    {
        var trialEnd = user.Subscription?.TrialEndsAt;

        await bot.SendMessage(
            chatId,
            $"👋 Добро пожаловать в ClientiX!\n\n" +
            $"🎁 Вам активирован бесплатный пробный период на 7 дней (до {trialEnd:dd.MM.yyyy}).\n\n" +
            $"📋 Давайте познакомимся, чтобы настроить ваш будущий бот. " +
            $"Это займёт меньше минуты.\n\n" +
            $"Шаг 1 из 3: какая у вас специализация?",
            replyMarkup: NicheKeyboard(),
            cancellationToken: ct);

        await _states.SetAsync(user.TelegramId, new UserState { CurrentStep = "niche" });
    }

    private static InlineKeyboardMarkup NicheKeyboard() =>
        new(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("💅 Маникюр / педикюр", "niche:nails") },
            new[] { InlineKeyboardButton.WithCallbackData("✂️ Парикмахер / барбер", "niche:barber") },
            new[] { InlineKeyboardButton.WithCallbackData("🎨 Татуировки", "niche:tattoo") },
            new[] { InlineKeyboardButton.WithCallbackData("👁 Ресницы / брови", "niche:lashes") },
            new[] { InlineKeyboardButton.WithCallbackData("💆 Косметология / массаж", "niche:beauty") },
            new[] { InlineKeyboardButton.WithCallbackData("🌸 Другое", "niche:other") },
        });

    /// <summary>
    /// Обработка callback'ов: выбор ниши в анкете или клик по главному меню.
    /// </summary>
    private async Task HandleCallbackAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        var data = callback.Data ?? string.Empty;
        var chatId = callback.Message!.Chat.Id;
        var telegramId = callback.From.Id;

        // Шаг анкеты — выбор ниши
        if (data.StartsWith("niche:"))
        {
            await HandleNicheChosenAsync(bot, callback, ct);
            return;
        }

        // Кнопки главного меню
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
        string reply = data switch
        {
            "connect_bot" =>
                "🤖 Чтобы подключить своего бота:\n\n" +
                "1) Откройте @BotFather\n" +
                "2) Создайте нового бота командой /newbot\n" +
                "3) Скопируйте полученный токен\n" +
                "4) Отправьте его сюда сообщением\n\n" +
                "(Функция в разработке)",
            "tariffs" =>
                "💎 Тарифы ClientiX\n\n" +
                "🎉 7 дней — бесплатный пробный период\n\n" +
                "Подписка (первая оплата / продление):\n" +
                "▪️ 30 дней — 300 руб. / 500 руб.\n" +
                "▪️ 90 дней — 1000 руб. / 1300 руб.\n" +
                "▪️ 180 дней — 2000 руб. / 2400 руб.",
            "cabinet" =>
                "📊 Личный кабинет\n\n(Функция в разработке)",
            "about" =>
                "ℹ️ ClientiX — SaaS-платформа аренды Telegram-ботов " +
                "для самозанятых мастеров бьюти-индустрии.\n\nВерсия: 0.3 (alpha)",
            _ => "Команда не распознана."
        };
        await bot.SendMessage(chatId, reply, cancellationToken: ct);
    }

    /// <summary>
    /// Анкета шаг 1: ниша выбрана, спрашиваем город.
    /// </summary>
    private async Task HandleNicheChosenAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        var nicheCode = callback.Data!.Replace("niche:", "");
        var telegramId = callback.From.Id;
        var chatId = callback.Message!.Chat.Id;

        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var state = await _states.GetAsync(telegramId);
        if (state is null || state.CurrentStep != "niche")
        {
            await bot.SendMessage(chatId,
                "Анкета устарела. Отправьте /start, чтобы начать заново.",
                cancellationToken: ct);
            return;
        }

        state.Data["niche"] = nicheCode;
        state.CurrentStep = "city";
        await _states.SetAsync(telegramId, state);

        await bot.SendMessage(chatId,
            "Отлично! ✨\n\n" +
            "Шаг 2 из 3: в каком городе вы работаете?\n" +
            "Напишите название одним сообщением (например: Москва).",
            cancellationToken: ct);
    }

    /// <summary>
    /// Обработка текстовых сообщений вне команды /start: продвижение по анкете.
    /// </summary>
    private async Task HandleTextMessageAsync(
        ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        if (message.From is null) return;

        var state = await _states.GetAsync(message.From.Id);

        if (state is null)
        {
            await bot.SendMessage(message.Chat.Id,
                "Я понимаю команду /start. Нажмите её, чтобы открыть меню.",
                cancellationToken: ct);
            return;
        }

        switch (state.CurrentStep)
        {
            case "city":
                await HandleCityAsync(bot, message, state, ct);
                break;
            case "phone":
                await HandlePhoneAsync(bot, message, state, ct);
                break;
            default:
                await bot.SendMessage(message.Chat.Id,
                    "Пожалуйста, выберите вариант кнопкой выше или отправьте /start.",
                    cancellationToken: ct);
                break;
        }
    }

    /// <summary>
    /// Анкета шаг 2: город получен, спрашиваем телефон.
    /// </summary>
    private async Task HandleCityAsync(
        ITelegramBotClient bot, Message message, UserState state, CancellationToken ct)
    {
        var city = (message.Text ?? "").Trim();
        if (city.Length < 2 || city.Length > 64)
        {
            await bot.SendMessage(message.Chat.Id,
                "Название города должно быть от 2 до 64 символов. Попробуйте ещё раз.",
                cancellationToken: ct);
            return;
        }

        state.Data["city"] = city;
        state.CurrentStep = "phone";
        await _states.SetAsync(message.From!.Id, state);

        await bot.SendMessage(message.Chat.Id,
            "Записал. 📍\n\n" +
            "Шаг 3 из 3: укажите контактный телефон.\n" +
            "В формате +7XXXXXXXXXX. Он нужен только для связи с вами.",
            cancellationToken: ct);
    }

    /// <summary>
    /// Анкета шаг 3: телефон получен, завершаем анкету, показываем меню.
    /// </summary>
    private async Task HandlePhoneAsync(
        ITelegramBotClient bot, Message message, UserState state, CancellationToken ct)
    {
        var phone = (message.Text ?? "").Trim();
        var digits = new string(phone.Where(char.IsDigit).ToArray());

        if (digits.Length < 10 || digits.Length > 15)
        {
            await bot.SendMessage(message.Chat.Id,
                "Похоже, в номере не хватает цифр. Введите в формате +7XXXXXXXXXX.",
                cancellationToken: ct);
            return;
        }

        // Сохраняем профиль в БД
        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        await users.UpdateMasterProfileAsync(
            telegramId: message.From!.Id,
            niche: state.Data.GetValueOrDefault("niche", "other"),
            city: state.Data.GetValueOrDefault("city", ""),
            phone: phone,
            ct: ct);

        await _states.ClearAsync(message.From.Id);

        await bot.SendMessage(message.Chat.Id,
            "🎉 Готово! Анкета заполнена.\n\n" +
            "Теперь можно подключать своего бота и принимать первые записи.",
            cancellationToken: ct);

        // Подгружаем пользователя свежим и шлём меню
        var user = await users.GetByTelegramIdAsync(message.From.Id, ct);
        if (user is not null)
        {
            await SendMainMenuAsync(bot, message.Chat.Id, user, ct);
        }
    }

    /// <summary>
    /// Главное меню для мастера, уже прошедшего анкету.
    /// </summary>
    private async Task SendMainMenuAsync(
        ITelegramBotClient bot, long chatId, DomainUser user, CancellationToken ct)
    {
        var trialEnd = user.Subscription?.TrialEndsAt;
        var daysLeft = trialEnd.HasValue
            ? Math.Max(0, (int)(trialEnd.Value - DateTime.UtcNow).TotalDays)
            : 0;

        var nicheText = user.ManagedBot?.Niche switch
        {
            "nails" => "💅 Маникюр / педикюр",
            "barber" => "✂️ Парикмахер / барбер",
            "tattoo" => "🎨 Татуировки",
            "lashes" => "👁 Ресницы / брови",
            "beauty" => "💆 Косметология / массаж",
            _ => "🌸 Другое"
        };

        var text =
            $"С возвращением, {user.FirstName ?? "мастер"}! 👋\n\n" +
            $"📍 Город: {user.ManagedBot?.City ?? "не указан"}\n" +
            $"💼 Специализация: {nicheText}\n" +
            $"📊 Статус: {GetStatusEmoji(user.Subscription?.Status)} {GetStatusName(user.Subscription?.Status)}\n" +
            $"📅 Действует до: {user.Subscription?.CurrentPeriodEnd:dd.MM.yyyy}\n" +
            (daysLeft > 0 ? $"⏳ Осталось дней: {daysLeft}\n" : "") +
            $"\nЧто вы хотите сделать?";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("✨ Подключить своего бота", "connect_bot") },
            new[] { InlineKeyboardButton.WithCallbackData("💎 Тарифы и подписка", "tariffs") },
            new[] { InlineKeyboardButton.WithCallbackData("📊 Мой кабинет", "cabinet") },
            new[] { InlineKeyboardButton.WithCallbackData("ℹ️ О сервисе", "about") },
        });

        await bot.SendMessage(chatId, text, replyMarkup: keyboard, cancellationToken: ct);
    }

    private static string GetStatusEmoji(string? status) => status switch
    {
        "trial" => "🎁",
        "active" => "✅",
        "expired" => "⏰",
        "cancelled" => "❌",
        _ => "❓"
    };

    private static string GetStatusName(string? status) => status switch
    {
        "trial" => "Триал",
        "active" => "Активна",
        "expired" => "Истекла",
        "cancelled" => "Отменена",
        _ => "Не определён"
    };

    // === Обработка сетевых ошибок (без изменений) ===
    private async Task HandleErrorAsync(
        ITelegramBotClient bot, Exception exception, HandleErrorSource source, CancellationToken ct)
    {
        bool isTransientNetworkError = IsTransient(exception);

        if (isTransientNetworkError)
        {
            _logger.LogWarning(
                "Сетевой обрыв соединения с Telegram API ({Type}): {Message}. Переподключаюсь через 5 секунд.",
                exception.GetType().Name,
                exception.Message);
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return;
        }

        _logger.LogError(exception, "Ошибка Telegram polling (source: {Source})", source);
        await Task.Delay(TimeSpan.FromSeconds(3), ct);
    }

    private static bool IsTransient(Exception? ex)
    {
        while (ex is not null)
        {
            if (ex is System.Net.Http.HttpRequestException
                or System.Net.Http.HttpIOException
                or TaskCanceledException
                or System.Net.Sockets.SocketException
                or Telegram.Bot.Exceptions.RequestException)
            {
                return true;
            }
            ex = ex.InnerException;
        }
        return false;
    }
}