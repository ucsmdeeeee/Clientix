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

        // FSM анкеты
        if (data.StartsWith("niche:"))
        {
            await HandleNicheChosenAsync(bot, callback, ct);
            return;
        }

        // Услуги: подтверждение удаления
        if (data.StartsWith("svc_del:"))
        {
            await HandleDeleteServiceAsync(bot, callback, ct);
            return;
        }

        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        switch (data)
        {
            case "connect_bot":
                await bot.SendMessage(chatId,
                    "🤖 Подключение бота\n\n" +
                    "1) Откройте @BotFather\n" +
                    "2) Создайте нового бота командой /newbot\n" +
                    "3) Скопируйте полученный токен\n" +
                    "4) Отправьте его сюда сообщением\n\n" +
                    "(Функция будет включена в следующей версии)",
                    cancellationToken: ct);
                break;

            case "tariffs":
                await bot.SendMessage(chatId,
                    "💎 Тарифы ClientiX\n\n" +
                    "🎉 7 дней — бесплатный пробный период\n\n" +
                    "Подписка (первая оплата / продление):\n" +
                    "▪️ 30 дней — 300 руб. / 500 руб.\n" +
                    "▪️ 90 дней — 1000 руб. / 1300 руб.\n" +
                    "▪️ 180 дней — 2000 руб. / 2400 руб.",
                    cancellationToken: ct);
                break;

            case "cabinet":
                await SendCabinetAsync(bot, chatId, telegramId, ct);
                break;

            case "about":
                await bot.SendMessage(chatId,
                    "ℹ️ ClientiX — SaaS-платформа аренды Telegram-ботов " +
                    "для самозанятых мастеров бьюти-индустрии.\n\n" +
                    "Версия: 0.4 (alpha)\n" +
                    "Разработка: дипломный проект ITHub, 2026",
                    cancellationToken: ct);
                break;

            case "main_menu":
                await ShowMainMenuFromCallbackAsync(bot, chatId, telegramId, ct);
                break;

            case "services":
                await SendServicesListAsync(bot, chatId, telegramId, ct);
                break;

            case "service_add":
                await StartAddServiceFsmAsync(bot, chatId, telegramId, ct);
                break;

            case "referral":
                await SendReferralInfoAsync(bot, chatId, telegramId, ct);
                break;

            case "subscription_info":
                await SendSubscriptionInfoAsync(bot, chatId, telegramId, ct);
                break;

            default:
                await bot.SendMessage(chatId,
                    "Команда не распознана. Отправьте /start для возврата в меню.",
                    cancellationToken: ct);
                break;
        }
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
            case "svc_name":
                await HandleServiceNameAsync(bot, message, state, ct);
                break;
            case "svc_duration":
                await HandleServiceDurationAsync(bot, message, state, ct);
                break;
            case "svc_price":
                await HandleServicePriceAsync(bot, message, state, ct);
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

    /// <summary>
    /// Показывает экран «Мой кабинет» с подразделами.
    /// </summary>
    private async Task SendCabinetAsync(
        ITelegramBotClient bot, long chatId, long telegramId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(telegramId, ct);
        if (user is null)
        {
            await bot.SendMessage(chatId, "Сначала нужно зарегистрироваться: отправьте /start.",
                cancellationToken: ct);
            return;
        }

        var services = await users.GetServicesAsync(user.Id, ct);
        var daysLeft = user.Subscription?.CurrentPeriodEnd > DateTime.UtcNow
            ? (int)(user.Subscription.CurrentPeriodEnd - DateTime.UtcNow).TotalDays
            : 0;

        var text =
            $"📊 Личный кабинет\n\n" +
            $"👤 {user.FirstName ?? "—"} {user.LastName ?? ""}\n" +
            $"📞 {user.Phone ?? "не указан"}\n" +
            $"📍 {user.ManagedBot?.City ?? "—"}\n" +
            $"💼 {NicheToText(user.ManagedBot?.Niche)}\n\n" +
            $"💎 Подписка: {GetStatusEmoji(user.Subscription?.Status)} {GetStatusName(user.Subscription?.Status)}\n" +
            $"📅 Действует до: {user.Subscription?.CurrentPeriodEnd:dd.MM.yyyy} ({daysLeft} дн.)\n\n" +
            $"📦 Услуг в каталоге: {services.Count}\n" +
            $"🤝 Реферальный код: {user.ReferralCode}";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
        new[] { InlineKeyboardButton.WithCallbackData("📋 Мои услуги", "services") },
        new[] { InlineKeyboardButton.WithCallbackData("💎 Моя подписка", "subscription_info") },
        new[] { InlineKeyboardButton.WithCallbackData("🤝 Пригласить мастера", "referral") },
        new[] { InlineKeyboardButton.WithCallbackData("« Назад в меню", "main_menu") },
    });

        await bot.SendMessage(chatId, text, replyMarkup: keyboard, cancellationToken: ct);
    }

    /// <summary>
    /// Показывает список услуг мастера с возможностью добавить или удалить.
    /// </summary>
    private async Task SendServicesListAsync(
        ITelegramBotClient bot, long chatId, long telegramId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(telegramId, ct);
        if (user is null) return;

        var services = await users.GetServicesAsync(user.Id, ct);

        string text;
        if (services.Count == 0)
        {
            text = "📋 Услуги\n\nПока ни одной услуги не добавлено.\n" +
                   "Нажмите «➕ Добавить услугу», чтобы создать первую.";
        }
        else
        {
            text = "📋 Ваши услуги:\n\n" +
                string.Join("\n", services.Select(s =>
                    $"• {s.Name} — {s.DurationMinutes} мин, {s.PriceRub} руб."));
            text += "\n\nЧтобы удалить услугу, нажмите кнопку с её названием ниже.";
        }

        var buttons = new List<InlineKeyboardButton[]>
    {
        new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить услугу", "service_add") }
    };

        foreach (var s in services.Take(10))
        {
            buttons.Add(new[] {
            InlineKeyboardButton.WithCallbackData(
                $"🗑 {Truncate(s.Name, 30)}", $"svc_del:{s.Id}")
        });
        }

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("« Назад в кабинет", "cabinet") });

        await bot.SendMessage(chatId, text,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task HandleDeleteServiceAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        if (!long.TryParse(callback.Data!.Replace("svc_del:", ""), out var serviceId))
            return;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(callback.From.Id, ct);
        if (user is null) return;

        var ok = await users.SoftDeleteServiceAsync(user.Id, serviceId, ct);
        var chatId = callback.Message!.Chat.Id;

        await bot.SendMessage(chatId,
            ok ? "🗑 Услуга удалена." : "Не удалось удалить услугу.",
            cancellationToken: ct);

        await SendServicesListAsync(bot, chatId, callback.From.Id, ct);
    }

    /// <summary>
    /// Запуск FSM добавления услуги.
    /// </summary>
    private async Task StartAddServiceFsmAsync(
        ITelegramBotClient bot, long chatId, long telegramId, CancellationToken ct)
    {
        var state = new UserState { CurrentStep = "svc_name" };
        await _states.SetAsync(telegramId, state);

        await bot.SendMessage(chatId,
            "➕ Добавление новой услуги\n\n" +
            "Шаг 1 из 3: как называется услуга?\n" +
            "Например: «Маникюр с покрытием гель-лак».",
            cancellationToken: ct);
    }

    private async Task SendReferralInfoAsync(
        ITelegramBotClient bot, long chatId, long telegramId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(telegramId, ct);
        if (user is null) return;

        var text =
            $"🤝 Реферальная программа\n\n" +
            $"Ваш персональный код:\n" +
            $"<code>{user.ReferralCode}</code>\n\n" +
            $"Расскажите о ClientiX другим мастерам, и вы получите 7 дополнительных дней подписки за каждого друга, который оплатит первый месяц.\n\n" +
            $"Программа активируется после полного релиза.";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
        new[] { InlineKeyboardButton.WithCallbackData("« Назад в кабинет", "cabinet") }
    });

        await bot.SendMessage(chatId, text,
            parseMode: ParseMode.Html,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task SendSubscriptionInfoAsync(
        ITelegramBotClient bot, long chatId, long telegramId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(telegramId, ct);
        if (user is null || user.Subscription is null) return;

        var s = user.Subscription;
        var daysLeft = s.CurrentPeriodEnd > DateTime.UtcNow
            ? (int)(s.CurrentPeriodEnd - DateTime.UtcNow).TotalDays
            : 0;

        var text =
            $"💎 Моя подписка\n\n" +
            $"📊 Статус: {GetStatusEmoji(s.Status)} {GetStatusName(s.Status)}\n" +
            $"📅 Действует до: {s.CurrentPeriodEnd:dd.MM.yyyy}\n" +
            $"⏳ Осталось: {daysLeft} дн.\n";

        if (s.Status == "trial")
            text += $"\n🎁 Сейчас идёт пробный период. После его окончания нужно выбрать тариф.";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
        new[] { InlineKeyboardButton.WithCallbackData("💎 Выбрать тариф", "tariffs") },
        new[] { InlineKeyboardButton.WithCallbackData("« Назад в кабинет", "cabinet") }
    });

        await bot.SendMessage(chatId, text, replyMarkup: keyboard, cancellationToken: ct);
    }

    /// <summary>
    /// Возврат в главное меню через callback (без regenerated /start).
    /// </summary>
    private async Task ShowMainMenuFromCallbackAsync(
        ITelegramBotClient bot, long chatId, long telegramId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(telegramId, ct);
        if (user is null) return;

        await SendMainMenuAsync(bot, chatId, user, ct);
    }

    private static string NicheToText(string? niche) => niche switch
    {
        "nails" => "💅 Маникюр / педикюр",
        "barber" => "✂️ Парикмахер / барбер",
        "tattoo" => "🎨 Татуировки",
        "lashes" => "👁 Ресницы / брови",
        "beauty" => "💆 Косметология / массаж",
        _ => "🌸 Другое"
    };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max - 1) + "…";

    private async Task HandleServiceNameAsync(
    ITelegramBotClient bot, Message message, UserState state, CancellationToken ct)
    {
        var name = (message.Text ?? "").Trim();
        if (name.Length < 2 || name.Length > 80)
        {
            await bot.SendMessage(message.Chat.Id,
                "Название услуги должно быть от 2 до 80 символов. Попробуйте ещё раз.",
                cancellationToken: ct);
            return;
        }

        state.Data["svc_name"] = name;
        state.CurrentStep = "svc_duration";
        await _states.SetAsync(message.From!.Id, state);

        await bot.SendMessage(message.Chat.Id,
            "Хорошо. ⏱\n\n" +
            "Шаг 2 из 3: сколько минут занимает эта услуга?\n" +
            "Введите число от 5 до 600 (например: 60).",
            cancellationToken: ct);
    }

    private async Task HandleServiceDurationAsync(
        ITelegramBotClient bot, Message message, UserState state, CancellationToken ct)
    {
        if (!int.TryParse(message.Text, out var duration) || duration < 5 || duration > 600)
        {
            await bot.SendMessage(message.Chat.Id,
                "Введите длительность в минутах: число от 5 до 600.",
                cancellationToken: ct);
            return;
        }

        state.Data["svc_duration"] = duration.ToString();
        state.CurrentStep = "svc_price";
        await _states.SetAsync(message.From!.Id, state);

        await bot.SendMessage(message.Chat.Id,
            "Принял. 💰\n\n" +
            "Шаг 3 из 3: сколько стоит услуга в рублях?\n" +
            "Введите целое число (например: 1500).",
            cancellationToken: ct);
    }

    private async Task HandleServicePriceAsync(
        ITelegramBotClient bot, Message message, UserState state, CancellationToken ct)
    {
        if (!int.TryParse(message.Text, out var price) || price < 0 || price > 1_000_000)
        {
            await bot.SendMessage(message.Chat.Id,
                "Введите цену в рублях: целое число от 0 до 1 000 000.",
                cancellationToken: ct);
            return;
        }

        var name = state.Data.GetValueOrDefault("svc_name", "Услуга");
        var duration = int.Parse(state.Data.GetValueOrDefault("svc_duration", "60"));

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(message.From!.Id, ct);
        if (user is null) return;

        var service = await users.AddServiceAsync(user.Id, name, duration, price, ct);
        await _states.ClearAsync(message.From.Id);

        _logger.LogInformation(
            "Добавлена услуга: user={UserId}, svc={SvcId}, name={Name}",
            user.Id, service.Id, service.Name);

        await bot.SendMessage(message.Chat.Id,
            $"✅ Услуга добавлена!\n\n" +
            $"📋 {service.Name}\n" +
            $"⏱ {service.DurationMinutes} мин\n" +
            $"💰 {service.PriceRub} руб.",
            cancellationToken: ct);

        await SendServicesListAsync(bot, message.Chat.Id, message.From.Id, ct);
    }
}