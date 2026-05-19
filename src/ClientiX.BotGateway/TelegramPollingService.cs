using ClientiX.BotGateway.MasterBots;
using ClientiX.Domain.Entities;
using ClientiX.Infrastructure.Persistence;
using ClientiX.Infrastructure.Repositories;
using ClientiX.Infrastructure.Security;
using ClientiX.Infrastructure.State;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using DomainUser = ClientiX.Domain.Entities.User;

namespace ClientiX.BotGateway;

public class TelegramPollingService : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly ILogger<TelegramPollingService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly UserStateService _states;
    private readonly TokenProtector _protector;
    private readonly MasterBotManager _masterBotManager;

    public TelegramPollingService(
        ITelegramBotClient bot,
        ILogger<TelegramPollingService> logger,
        IServiceScopeFactory scopeFactory,
        UserStateService states,
        TokenProtector protector,
        MasterBotManager masterBotManager)
    {
        _bot = bot;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _states = states;
        _protector = protector;
        _masterBotManager = masterBotManager;
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
            if (update.Message is { } message)
            {
                if (message.Text is not null)
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
                else if (message.Photo is { Length: > 0 } photos)
                {
                    // Telegram присылает несколько размеров одного фото —
                    // берём самый большой (последний в массиве)
                    var largest = photos[^1];
                    await HandlePhotoUploadAsync(bot, message, largest.FileId, ct);
                }
                else if (message.Document is { } doc &&
                         doc.MimeType is not null &&
                         doc.MimeType.StartsWith("image/"))
                {
                    // Фото, отправленное как файл
                    await HandlePhotoUploadAsync(bot, message, doc.FileId, ct);
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

        if (data.StartsWith("sched_day:"))
        {
            await HandleScheduleDayClickAsync(bot, callback, ct);
            return;
        }

        if (data.StartsWith("sched_set:"))
        {
            await HandleScheduleSetAsync(bot, callback, ct);
            return;
        }

        if (data.StartsWith("excpt_del:"))
        {
            await HandleExceptionDeleteAsync(bot, callback, ct);
            return;
        }

        if (data.StartsWith("pfl_del:"))
        {
            await HandlePortfolioDeleteAsync(bot, callback, ct);
            return;
        }

        if (data.StartsWith("pfl_view:"))
        {
            await HandlePortfolioViewAsync(bot, callback, ct);
            return;
        }

        if (data.StartsWith("master_cancel_booking:"))
        {
            await HandleMasterCancelBookingAsync(bot, callback, ct);
            return;
        }

        if (data.StartsWith("tz_set:"))
        {
            await HandleTimezoneSetAsync(bot, callback, ct);
            return;
        }

        if (data.StartsWith("horizon_set:"))
        {
            await HandleHorizonSetAsync(bot, callback, ct);
            return;
        }

        if (data.StartsWith("cal_nav:"))
        {
            await HandleCalendarNavAsync(bot, callback, ct);
            return;
        }

        if (data.StartsWith("cal_day:"))
        {
            await HandleCalendarDayClickAsync(bot, callback, ct);
            return;
        }

        if (data.StartsWith("day_set:"))
        {
            await HandleDaySetAsync(bot, callback, ct);
            return;
        }

        if (data.StartsWith("master_add_service:"))
        {
            await ShowMasterAddServiceMenuAsync(bot, callback, ct);
            return;
        }

        if (data.StartsWith("m_add_svc:"))
        {
            await HandleMasterAddServiceConfirmAsync(bot, callback, ct);
            return;
        }

        if (data.StartsWith("master_remove_service:"))
        {
            await ShowMasterRemoveServiceMenuAsync(bot, callback, ct);
            return;
        }

        if (data.StartsWith("m_rm_svc:"))
        {
            await HandleMasterRemoveServiceConfirmAsync(bot, callback, ct);
            return;
        }

        if (data.StartsWith("master_reschedule:"))
        {
            await StartMasterRescheduleAsync(bot, callback, ct);
            return;
        }

        if (data.StartsWith("m_resched_date:"))
        {
            await HandleMasterRescheduleDateAsync(bot, callback, ct);
            return;
        }

        if (data.StartsWith("m_resched_slot:"))
        {
            await HandleMasterRescheduleSlotAsync(bot, callback, ct);
            return;
        }

        if (data == "m_resched_confirm")
        {
            await HandleMasterRescheduleConfirmAsync(bot, callback, ct);
            return;
        }

        if (data.StartsWith("rem_24:"))
        {
            await HandleReminder24SetAsync(bot, callback, ct);
            return;
        }

        if (data.StartsWith("rem_extra:"))
        {
            await HandleReminderExtraSetAsync(bot, callback, ct);
            return;
        }

        if (data.StartsWith("master_complete:"))
        {
            await HandleMasterCompleteBookingAsync(bot, callback, "completed", ct);
            return;
        }

        if (data.StartsWith("master_noshow:"))
        {
            await HandleMasterCompleteBookingAsync(bot, callback, "no_show", ct);
            return;
        }

        if (data == "noop")
        {
            return;
        }

        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        switch (data)
        {
            case "connect_bot":
                await StartConnectBotFsmAsync(bot, chatId, telegramId, ct);
                break;

            case "tariffs":
                await SendTariffsAsync(bot, chatId, telegramId, ct);
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

            case "buy_30":
            case "buy_90":
            case "buy_180":
                await StartPaymentAsync(bot, chatId, telegramId, data, ct);
                break;

            case "schedule":
                await SendCalendarAsync(bot, chatId, telegramId, DateTime.UtcNow, ct);
                break;

            case "schedule_week_template":
                await SendWeekTemplateAsync(bot, chatId, telegramId, ct);
                break;

            case "schedule_exceptions":
                await SendExceptionsListAsync(bot, chatId, telegramId, ct);
                break;

            case "schedule_exception_add":
                await StartAddExceptionFsmAsync(bot, chatId, telegramId, ct);
                break;

            case "portfolio":
                await SendPortfolioAsync(bot, chatId, telegramId, ct);
                break;

            case "portfolio_add":
                await StartAddPortfolioFsmAsync(bot, chatId, telegramId, ct);
                break;

            case "my_bookings":
                await SendMasterBookingsAsync(bot, chatId, telegramId, ct);
                break;

            case "timezone_change":
                await SendTimezonePickerAsync(bot, chatId, telegramId, ct);
                break;

            case "horizon":
                await SendHorizonPickerAsync(bot, chatId, telegramId, ct);
                break;

            case "reminders":
                await SendRemindersMenuAsync(bot, chatId, telegramId, ct);
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
            case "connect_bot_token":
                await HandleBotTokenAsync(bot, message, state, ct);
                break;
            case "sched_hours":
                await HandleScheduleHoursInputAsync(bot, message, state, ct);
                break;
            case "excpt_date":
                await HandleExceptionDateAsync(bot, message, state, ct);
                break;
            case "excpt_hours":
                await HandleExceptionHoursAsync(bot, message, state, ct);
                break;
            case "excpt_note":
                await HandleExceptionNoteAsync(bot, message, state, ct);
                break;
            case "pfl_caption":
                await HandlePortfolioCaptionAsync(bot, message, state, ct);
                break;
            case "day_set_hours":
                await HandleDaySetHoursAsync(bot, message, state, ct);
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

        var nicheText = NicheToText(user.ManagedBot?.Niche);

        var botStatusLine = !string.IsNullOrEmpty(user.ManagedBot?.BotUsername)
            ? $"🤖 Ваш бот: @{user.ManagedBot.BotUsername}\n"
            : "🤖 Бот пока не подключён\n";

        var text =
            $"С возвращением, {user.FirstName ?? "мастер"}! 👋\n\n" +
            botStatusLine +
            $"📍 Город: {user.ManagedBot?.City ?? "не указан"}\n" +
            $"💼 Специализация: {nicheText}\n" +
            $"📊 Статус: {GetStatusEmoji(user.Subscription?.Status)} {GetStatusName(user.Subscription?.Status)}\n" +
            $"📅 Действует до: {user.Subscription?.CurrentPeriodEnd:dd.MM.yyyy}\n" +
            (daysLeft > 0 ? $"⏳ Осталось дней: {daysLeft}\n" : "") +
            $"\nЧто вы хотите сделать?";

        var connectBotLabel = string.IsNullOrEmpty(user.ManagedBot?.BotUsername)
            ? "✨ Подключить своего бота"
            : "🔄 Переподключить бота";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
        new[] { InlineKeyboardButton.WithCallbackData(connectBotLabel, "connect_bot") },
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
    new[] { InlineKeyboardButton.WithCallbackData("📒 Мои записи", "my_bookings") },
    new[] { InlineKeyboardButton.WithCallbackData("📋 Мои услуги", "services") },
    new[] { InlineKeyboardButton.WithCallbackData("🖼 Моё портфолио", "portfolio") },
    new[] { InlineKeyboardButton.WithCallbackData("📅 Моё расписание", "schedule") },
    new[] { InlineKeyboardButton.WithCallbackData("⏰ Горизонт записи", "horizon") },
    new[] { InlineKeyboardButton.WithCallbackData("💎 Моя подписка", "subscription_info") },
    new[] { InlineKeyboardButton.WithCallbackData("🤝 Пригласить мастера", "referral") },
    new[] { InlineKeyboardButton.WithCallbackData("🌍 Часовой пояс", "timezone_change") },
    new[] { InlineKeyboardButton.WithCallbackData("🔔 Напоминания клиентам", "reminders") },
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

    /// <summary>
    /// Запуск FSM подключения бота мастера.
    /// </summary>
    private async Task StartConnectBotFsmAsync(
        ITelegramBotClient bot, long chatId, long telegramId, CancellationToken ct)
    {
        var state = new UserState { CurrentStep = "connect_bot_token" };
        await _states.SetAsync(telegramId, state);

        await bot.SendMessage(chatId,
            "🤖 Подключение вашего бота\n\n" +
            "Пожалуйста, выполните следующие шаги:\n\n" +
            "1️⃣ Откройте @BotFather в Telegram\n" +
            "2️⃣ Отправьте команду /newbot\n" +
            "3️⃣ Введите название (например, «Маникюр у Анны»)\n" +
            "4️⃣ Введите username бота (должен оканчиваться на _bot, например anna_nails_bot)\n" +
            "5️⃣ BotFather пришлёт токен вида 7123456789:AAH...\n\n" +
            "📥 Скопируйте этот токен и отправьте его одним сообщением сюда.\n\n" +
            "🔒 Ваш токен будет зашифрован и сохранён только в нашей защищённой базе данных.",
            cancellationToken: ct);
    }

    /// <summary>
    /// Обработка присланного токена: валидация через Telegram API, шифрование, сохранение.
    /// </summary>
    private async Task HandleBotTokenAsync(
        ITelegramBotClient bot, Message message, UserState state, CancellationToken ct)
    {
        var token = (message.Text ?? "").Trim();

        // Поверхностная валидация формата токена: число:строка
        if (!System.Text.RegularExpressions.Regex.IsMatch(token, @"^\d{6,}:[A-Za-z0-9_\-]{30,}$"))
        {
            await bot.SendMessage(message.Chat.Id,
                "🚫 Это не похоже на токен Telegram-бота.\n\n" +
                "Токен выглядит так: 7123456789:AAH-длинная-строка\n" +
                "Попробуйте ещё раз.",
                cancellationToken: ct);
            return;
        }

        // Сообщение о валидации
        await bot.SendMessage(message.Chat.Id,
            "🔍 Проверяю токен через Telegram API…",
            cancellationToken: ct);

        // Валидация через getMe
        Telegram.Bot.Types.User botInfo;
        try
        {
            var tempClient = new TelegramBotClient(token);
            botInfo = await tempClient.GetMe(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Токен невалиден: user={UserId}, error={Error}",
                message.From!.Id, ex.Message);

            await bot.SendMessage(message.Chat.Id,
                "🚫 Токен невалиден. Telegram отверг его.\n\n" +
                "Возможные причины: токен скопирован не полностью, бот был удалён в BotFather, " +
                "токен был отозван (тогда сгенерируйте новый через /revoke в BotFather).\n\n" +
                "Попробуйте ещё раз или нажмите /start, чтобы выйти.",
                cancellationToken: ct);
            return;
        }

        // Проверка, что бот ещё не подключён другим мастером
        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(message.From!.Id, ct);
        if (user is null) return;

        if (await users.IsBotAlreadyAttachedAsync(botInfo.Id, user.Id, ct))
        {
            await bot.SendMessage(message.Chat.Id,
                "⚠️ Этот бот уже подключён к другому аккаунту в ClientiX.\n\n" +
                "Если это ваш бот — обратитесь в поддержку.",
                cancellationToken: ct);
            return;
        }

        // Шифрование токена и генерация webhook-секрета
        var encryptedToken = _protector.Encrypt(token);
        var webhookSecret = GenerateWebhookSecret();

        await users.AttachBotAsync(
            userId: user.Id,
            botTelegramId: botInfo.Id,
            botUsername: botInfo.Username ?? "",
            encryptedToken: encryptedToken,
            webhookSecret: webhookSecret,
            ct: ct);

        await _states.ClearAsync(message.From.Id);

        _logger.LogInformation(
            "Бот мастера подключён: user={UserId}, bot=@{BotUsername}, botId={BotId}",
            user.Id, botInfo.Username, botInfo.Id);

        var botLink = botInfo.Username is not null
            ? $"https://t.me/{botInfo.Username}"
            : "";

        await bot.SendMessage(message.Chat.Id,
            $"🎉 Готово! Ваш бот подключён.\n\n" +
            $"🤖 Имя: {botInfo.FirstName}\n" +
            $"📛 Username: @{botInfo.Username}\n" +
            $"🔗 Ссылка: {botLink}\n\n" +
            $"🔒 Токен зашифрован и сохранён в защищённой базе данных платформы.\n\n" +
            $"Дальше можно настроить услуги и расписание в кабинете.",
            cancellationToken: ct);

        // Загружаем обновлённого пользователя и показываем меню
        var updated = await users.GetByTelegramIdAsync(message.From.Id, ct);
        if (updated is not null)
        {
            await SendMainMenuAsync(bot, message.Chat.Id, updated, ct);
        }

        await _masterBotManager.StartOneAsync(
            user.Id, botInfo.Id, botInfo.Username ?? "", encryptedToken);
    }

    /// <summary>
    /// Генерирует случайный 32-символьный секрет для проверки X-Telegram-Bot-Api-Secret-Token.
    /// </summary>
    private static string GenerateWebhookSecret()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = Random.Shared;
        return new string(Enumerable.Range(0, 32)
            .Select(_ => chars[random.Next(chars.Length)])
            .ToArray());
    }

    /// <summary>
    /// Показывает тарифные планы с кнопками оформления подписки.
    /// </summary>
    private async Task SendTariffsAsync(
        ITelegramBotClient bot, long chatId, long telegramId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(telegramId, ct);

        // Дифференцированные цены: первая оплата дешевле продления
        bool isFirstPayment = user?.HasMadeFirstPayment == false;

        var price30 = isFirstPayment ? 300 : 500;
        var price90 = isFirstPayment ? 1000 : 1300;
        var price180 = isFirstPayment ? 2000 : 2400;

        var text =
            $"💎 Тарифы ClientiX\n\n" +
            $"🎉 7 дней — бесплатный пробный период\n\n" +
            (isFirstPayment ? "🎁 Специальная цена первой оплаты:\n" : "💼 Цена продления:\n") +
            $"\n" +
            $"📅 30 дней — {price30} руб.\n" +
            $"📅 90 дней — {price90} руб.\n" +
            $"📅 180 дней — {price180} руб.\n\n" +
            $"💳 Оплата через ЮKassa. Чек придёт на email после оплаты.";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
        new[] { InlineKeyboardButton.WithCallbackData($"📅 30 дней за {price30} руб.", "buy_30") },
        new[] { InlineKeyboardButton.WithCallbackData($"📅 90 дней за {price90} руб.", "buy_90") },
        new[] { InlineKeyboardButton.WithCallbackData($"📅 180 дней за {price180} руб.", "buy_180") },
        new[] { InlineKeyboardButton.WithCallbackData("« Назад в меню", "main_menu") }
    });

        await bot.SendMessage(chatId, text, replyMarkup: keyboard, cancellationToken: ct);
    }

    /// <summary>
    /// Создаёт платёж в БД и отправляет пользователю ссылку на оплату.
    /// </summary>
    private async Task StartPaymentAsync(
        ITelegramBotClient bot, long chatId, long telegramId, string data, CancellationToken ct)
    {
        var tariffCode = data switch
        {
            "buy_30" => "days_30",
            "buy_90" => "days_90",
            "buy_180" => "days_180",
            _ => null
        };
        if (tariffCode is null) return;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var payments = scope.ServiceProvider.GetRequiredService<PaymentRepository>();
        var ykService = scope.ServiceProvider
            .GetRequiredService<ClientiX.Infrastructure.Payments.YooKassaPaymentService>();

        var user = await users.GetByTelegramIdAsync(telegramId, ct);
        if (user is null) return;

        var tariff = await payments.GetTariffByCodeAsync(tariffCode, ct);
        if (tariff is null)
        {
            await bot.SendMessage(chatId, "Тариф не найден.", cancellationToken: ct);
            return;
        }

        bool isFirstPayment = !user.HasMadeFirstPayment;
        int amountRub = isFirstPayment ? tariff.PriceFirstRub : tariff.PriceRenewRub;

        var payment = await payments.CreatePendingPaymentAsync(
            user.Id, tariff.Id, amountRub, isFirstPayment, ct);

        var description = $"ClientiX: подписка на {tariff.DurationDays} дней";
        var paymentLink = await ykService.CreatePaymentLinkAsync(payment, description, ct);

        _logger.LogInformation(
            "Создан платёж: id={PaymentId}, user={UserId}, tariff={Tariff}, amount={Amount}",
            payment.Id, user.Id, tariff.Code, amountRub);

        // Telegram не принимает http://localhost в URL-кнопках, поэтому даём ссылку
        // текстом + отдельную кнопку «назад». На проде здесь будет реальный HTTPS-URL от ЮKassa.
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithUrl("💳 Перейти к оплате", paymentLink) },
            new[] { InlineKeyboardButton.WithCallbackData("« Назад в меню", "main_menu") }
        });

        await bot.SendMessage(chatId,
            $"💳 Оформление подписки\n\n" +
            $"📅 Тариф: {tariff.DurationDays} дней\n" +
            $"💰 Сумма: {amountRub} руб.\n" +
            $"#️⃣ Номер платежа: {payment.Id}\n\n" +
            $"Нажмите кнопку ниже, чтобы перейти к оплате. " +
            $"После успешной оплаты подписка будет активирована автоматически.",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    // ============================================================
    // РАСПИСАНИЕ МАСТЕРА
    // ============================================================

    private static readonly string[] DayNames =
        { "Воскресенье", "Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота" };

    private static readonly string[] DayNamesShort =
        { "Вс", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб" };

    private async Task SendScheduleAsync(
        ITelegramBotClient bot, long chatId, long telegramId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(telegramId, ct);
        if (user is null) return;

        var schedule = await users.GetWeeklyScheduleAsync(user.Id, ct);
        var exceptions = await users.GetUpcomingExceptionsAsync(user.Id, ct);

        // Сортировка по дням, начиная с понедельника
        var dayOrder = new[] { 1, 2, 3, 4, 5, 6, 0 };
        var ordered = dayOrder.Select(d => schedule.First(s => s.DayOfWeek == d)).ToList();

        var text = "📅 Моё расписание (шаблон недели)\n\n" +
            string.Join("\n", ordered.Select(s =>
                s.IsWorking
                    ? $"✅ {DayNames[s.DayOfWeek]}: {FormatTime(s.FromMinutes)}–{FormatTime(s.ToMinutes)}"
                    : $"⛔ {DayNames[s.DayOfWeek]}: выходной"));

        if (exceptions.Any())
        {
            text += $"\n\n📌 Ближайшие исключения: {exceptions.Count}";
        }

        text += "\n\nНажмите на день, чтобы изменить.";

        var buttons = new List<InlineKeyboardButton[]>();
        foreach (var s in ordered)
        {
            var emoji = s.IsWorking ? "✅" : "⛔";
            var hours = s.IsWorking ? $" {FormatTime(s.FromMinutes)}–{FormatTime(s.ToMinutes)}" : " (вых.)";
            buttons.Add(new[] {
            InlineKeyboardButton.WithCallbackData(
                $"{emoji} {DayNames[s.DayOfWeek]}{hours}",
                $"sched_day:{s.DayOfWeek}")
        });
        }
        buttons.Add(new[] {
        InlineKeyboardButton.WithCallbackData("📌 Исключения на даты", "schedule_exceptions")
    });
        buttons.Add(new[] {
        InlineKeyboardButton.WithCallbackData("« Назад в кабинет", "cabinet")
    });

        await bot.SendMessage(chatId, text,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task HandleScheduleDayClickAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        if (!int.TryParse(callback.Data!.Replace("sched_day:", ""), out var dayOfWeek)) return;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(callback.From.Id, ct);
        if (user is null) return;

        var schedule = await users.GetWeeklyScheduleAsync(user.Id, ct);
        var day = schedule.First(s => s.DayOfWeek == dayOfWeek);

        var text = $"📅 {DayNames[dayOfWeek]}\n\n" +
            (day.IsWorking
                ? $"Сейчас: ✅ рабочий, {FormatTime(day.FromMinutes)}–{FormatTime(day.ToMinutes)}"
                : "Сейчас: ⛔ выходной");

        var keyboard = new InlineKeyboardMarkup(new[]
        {
        new[] {
            InlineKeyboardButton.WithCallbackData("✅ Сделать рабочим", $"sched_set:{dayOfWeek}:work"),
            InlineKeyboardButton.WithCallbackData("⛔ Сделать выходным", $"sched_set:{dayOfWeek}:off")
        },
        new[] {
            InlineKeyboardButton.WithCallbackData("🕐 Изменить часы", $"sched_set:{dayOfWeek}:hours")
        },
        new[] { InlineKeyboardButton.WithCallbackData("« К расписанию", "schedule") }
    });

        await bot.SendMessage(callback.Message!.Chat.Id, text,
            replyMarkup: keyboard, cancellationToken: ct);
    }

    private async Task HandleScheduleSetAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var parts = callback.Data!.Replace("sched_set:", "").Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var dayOfWeek)) return;

        var action = parts[1];
        var chatId = callback.Message!.Chat.Id;
        var telegramId = callback.From.Id;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(telegramId, ct);
        if (user is null) return;

        var schedule = await users.GetWeeklyScheduleAsync(user.Id, ct);
        var day = schedule.First(s => s.DayOfWeek == dayOfWeek);

        if (action == "work")
        {
            // Если выходные часы 0-0 — дефолт 10:00–20:00
            int from = day.FromMinutes > 0 ? day.FromMinutes : 600;
            int to = day.ToMinutes > 0 ? day.ToMinutes : 1200;
            await users.SetWeeklyDayAsync(user.Id, dayOfWeek, true, from, to, ct);
            await bot.SendMessage(chatId,
                $"✅ {DayNames[dayOfWeek]}: рабочий, {FormatTime(from)}–{FormatTime(to)}",
                cancellationToken: ct);
            await SendScheduleAsync(bot, chatId, telegramId, ct);
        }
        else if (action == "off")
        {
            await users.SetWeeklyDayAsync(user.Id, dayOfWeek, false, 0, 0, ct);
            await bot.SendMessage(chatId, $"⛔ {DayNames[dayOfWeek]}: выходной",
                cancellationToken: ct);
            await SendScheduleAsync(bot, chatId, telegramId, ct);
        }
        else if (action == "hours")
        {
            var state = new UserState
            {
                CurrentStep = "sched_hours",
                Data = { ["dayOfWeek"] = dayOfWeek.ToString() }
            };
            await _states.SetAsync(telegramId, state);

            await bot.SendMessage(chatId,
                $"🕐 Введите часы работы на {DayNames[dayOfWeek]} в формате\n" +
                "<code>ЧЧ:ММ-ЧЧ:ММ</code>\n\nНапример: <code>10:00-20:00</code>",
                parseMode: ParseMode.Html,
                cancellationToken: ct);
        }
    }

    private async Task HandleScheduleHoursInputAsync(
        ITelegramBotClient bot, Message message, UserState state, CancellationToken ct)
    {
        var input = (message.Text ?? "").Trim();
        var match = System.Text.RegularExpressions.Regex.Match(
            input, @"^(\d{1,2}):(\d{2})\s*[-–—]\s*(\d{1,2}):(\d{2})$");

        if (!match.Success)
        {
            await bot.SendMessage(message.Chat.Id,
                "Неверный формат. Введите как: 10:00-20:00", cancellationToken: ct);
            return;
        }

        int fromH = int.Parse(match.Groups[1].Value);
        int fromM = int.Parse(match.Groups[2].Value);
        int toH = int.Parse(match.Groups[3].Value);
        int toM = int.Parse(match.Groups[4].Value);

        if (fromH < 0 || fromH > 23 || fromM < 0 || fromM > 59 ||
            toH < 0 || toH > 23 || toM < 0 || toM > 59)
        {
            await bot.SendMessage(message.Chat.Id,
                "Время вне диапазона. Часы 0–23, минуты 0–59.", cancellationToken: ct);
            return;
        }

        int from = fromH * 60 + fromM;
        int to = toH * 60 + toM;
        if (to <= from)
        {
            await bot.SendMessage(message.Chat.Id,
                "Время окончания должно быть позже начала.", cancellationToken: ct);
            return;
        }

        if (!int.TryParse(state.Data.GetValueOrDefault("dayOfWeek", "1"), out var dayOfWeek)) return;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(message.From!.Id, ct);
        if (user is null) return;

        await users.SetWeeklyDayAsync(user.Id, dayOfWeek, true, from, to, ct);
        await _states.ClearAsync(message.From.Id);

        await bot.SendMessage(message.Chat.Id,
            $"✅ Часы обновлены: {DayNames[dayOfWeek]}, {FormatTime(from)}–{FormatTime(to)}",
            cancellationToken: ct);

        await SendScheduleAsync(bot, message.Chat.Id, message.From.Id, ct);
    }

    private async Task SendExceptionsListAsync(
        ITelegramBotClient bot, long chatId, long telegramId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(telegramId, ct);
        if (user is null) return;

        var exceptions = await users.GetUpcomingExceptionsAsync(user.Id, ct);

        string text;
        if (exceptions.Count == 0)
        {
            text = "📌 Исключения в расписании\n\n" +
                   "Пока ни одного исключения не задано.\n\n" +
                   "Используйте исключения, чтобы:\n" +
                   "▪️ закрыть рабочий день в отпуск или болезнь\n" +
                   "▪️ открыть выходной для дополнительного приёма\n" +
                   "▪️ изменить часы на конкретную дату";
        }
        else
        {
            text = "📌 Ближайшие исключения:\n\n" +
                string.Join("\n", exceptions.Select(e =>
                {
                    var note = string.IsNullOrEmpty(e.Note) ? "" : $" — {e.Note}";
                    return e.IsWorking
                        ? $"✅ {e.Date:dd.MM.yyyy}: {FormatTime(e.FromMinutes)}–{FormatTime(e.ToMinutes)}{note}"
                        : $"⛔ {e.Date:dd.MM.yyyy}: не работаю{note}";
                }));
        }

        var buttons = new List<InlineKeyboardButton[]>
    {
        new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить исключение", "schedule_exception_add") }
    };

        foreach (var e in exceptions.Take(10))
        {
            buttons.Add(new[] {
            InlineKeyboardButton.WithCallbackData(
                $"🗑 {e.Date:dd.MM}", $"excpt_del:{e.Id}")
        });
        }

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("« К расписанию", "schedule") });

        await bot.SendMessage(chatId, text,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task HandleExceptionDeleteAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        if (!long.TryParse(callback.Data!.Replace("excpt_del:", ""), out var id)) return;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(callback.From.Id, ct);
        if (user is null) return;

        var ok = await users.DeleteExceptionAsync(user.Id, id, ct);
        var chatId = callback.Message!.Chat.Id;

        await bot.SendMessage(chatId,
            ok ? "🗑 Исключение удалено." : "Не удалось удалить.",
            cancellationToken: ct);

        await SendExceptionsListAsync(bot, chatId, callback.From.Id, ct);
    }

    private async Task StartAddExceptionFsmAsync(
        ITelegramBotClient bot, long chatId, long telegramId, CancellationToken ct)
    {
        var state = new UserState { CurrentStep = "excpt_date" };
        await _states.SetAsync(telegramId, state);

        await bot.SendMessage(chatId,
            "📌 Добавление исключения\n\n" +
            "Шаг 1 из 3: на какую дату?\n" +
            "Введите в формате ДД.ММ.ГГГГ (например: 28.12.2026)",
            cancellationToken: ct);
    }

    private async Task HandleExceptionDateAsync(
        ITelegramBotClient bot, Message message, UserState state, CancellationToken ct)
    {
        var input = (message.Text ?? "").Trim();
        if (!DateTime.TryParseExact(input, "dd.MM.yyyy", null,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var date))
        {
            await bot.SendMessage(message.Chat.Id,
                "Не понял дату. Формат: ДД.ММ.ГГГГ, например 28.12.2026",
                cancellationToken: ct);
            return;
        }

        if (date.Date < DateTime.UtcNow.Date)
        {
            await bot.SendMessage(message.Chat.Id,
                "Дата уже прошла. Введите будущую.",
                cancellationToken: ct);
            return;
        }

        state.Data["date"] = date.Date.ToString("O");
        state.CurrentStep = "excpt_hours";
        await _states.SetAsync(message.From!.Id, state);

        await bot.SendMessage(message.Chat.Id,
            "Шаг 2 из 3: часы.\n\n" +
            "▪️ Введите <code>выходной</code> — закрыть день\n" +
            "▪️ Или часы в формате <code>10:00-20:00</code> — особые часы на эту дату",
            parseMode: ParseMode.Html,
            cancellationToken: ct);
    }

    private async Task HandleExceptionHoursAsync(
        ITelegramBotClient bot, Message message, UserState state, CancellationToken ct)
    {
        var input = (message.Text ?? "").Trim().ToLower();

        bool isWorking;
        int from = 0, to = 0;

        if (input == "выходной" || input == "off" || input == "не работаю")
        {
            isWorking = false;
        }
        else
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                input, @"^(\d{1,2}):(\d{2})\s*[-–—]\s*(\d{1,2}):(\d{2})$");
            if (!match.Success)
            {
                await bot.SendMessage(message.Chat.Id,
                    "Не понял. Напишите «выходной» или часы вида 10:00-20:00",
                    cancellationToken: ct);
                return;
            }

            from = int.Parse(match.Groups[1].Value) * 60 + int.Parse(match.Groups[2].Value);
            to = int.Parse(match.Groups[3].Value) * 60 + int.Parse(match.Groups[4].Value);

            if (from < 0 || from > 1439 || to < 0 || to > 1439 || to <= from)
            {
                await bot.SendMessage(message.Chat.Id,
                    "Время некорректно. Часы 0–23, минуты 0–59, конец позже начала.",
                    cancellationToken: ct);
                return;
            }
            isWorking = true;
        }

        state.Data["isWorking"] = isWorking.ToString();
        state.Data["from"] = from.ToString();
        state.Data["to"] = to.ToString();
        state.CurrentStep = "excpt_note";
        await _states.SetAsync(message.From!.Id, state);

        await bot.SendMessage(message.Chat.Id,
            "Шаг 3 из 3: комментарий (необязательно).\n\n" +
            "Например: «отпуск», «болезнь», «доп. приём».\n" +
            "Или отправьте «-» чтобы пропустить.",
            cancellationToken: ct);
    }

    private async Task HandleExceptionNoteAsync(
        ITelegramBotClient bot, Message message, UserState state, CancellationToken ct)
    {
        var note = (message.Text ?? "").Trim();
        if (note == "-" || note.ToLower() == "пропустить") note = "";

        if (!DateTime.TryParse(state.Data["date"], null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var date)) return;
        if (!bool.TryParse(state.Data["isWorking"], out var isWorking)) return;
        if (!int.TryParse(state.Data["from"], out var from)) return;
        if (!int.TryParse(state.Data["to"], out var to)) return;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(message.From!.Id, ct);
        if (user is null) return;

        await users.UpsertExceptionAsync(
            user.Id, date, isWorking, from, to,
            string.IsNullOrEmpty(note) ? null : note, ct);

        await _states.ClearAsync(message.From.Id);

        var summary = isWorking
            ? $"{date:dd.MM.yyyy}: {FormatTime(from)}–{FormatTime(to)}"
            : $"{date:dd.MM.yyyy}: выходной";
        if (!string.IsNullOrEmpty(note)) summary += $" ({note})";

        await bot.SendMessage(message.Chat.Id,
            $"✅ Исключение добавлено!\n\n📌 {summary}",
            cancellationToken: ct);

        await SendExceptionsListAsync(bot, message.Chat.Id, message.From.Id, ct);
    }

    private static string FormatTime(int minutes)
    {
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }

    // ============================================================
    // ПОРТФОЛИО МАСТЕРА
    // ============================================================

    private async Task SendPortfolioAsync(
        ITelegramBotClient bot, long chatId, long telegramId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(telegramId, ct);
        if (user is null) return;

        var items = await users.GetPortfolioAsync(user.Id, ct);

        string text;
        if (items.Count == 0)
        {
            text = "🖼 Моё портфолио\n\n" +
                   "Пока ни одной работы не загружено.\n\n" +
                   "Загруженные фото будут видны вашим клиентам " +
                   "в карточке мастера. Это сильно повышает доверие.";
        }
        else
        {
            text = $"🖼 Ваше портфолио: {items.Count} {GetWorksWord(items.Count)}\n\n" +
                   "Нажмите на работу, чтобы посмотреть или удалить.";
        }

        var buttons = new List<InlineKeyboardButton[]>
    {
        new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить фото", "portfolio_add") }
    };

        int idx = 1;
        foreach (var item in items.Take(10))
        {
            var label = string.IsNullOrEmpty(item.Caption)
                ? $"📷 Работа #{idx}"
                : $"📷 {Truncate(item.Caption, 30)}";
            buttons.Add(new[] {
            InlineKeyboardButton.WithCallbackData(label, $"pfl_view:{item.Id}")
        });
            idx++;
        }

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("« Назад в кабинет", "cabinet") });

        await bot.SendMessage(chatId, text,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task HandlePortfolioViewAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        if (!long.TryParse(callback.Data!.Replace("pfl_view:", ""), out var itemId)) return;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(callback.From.Id, ct);
        if (user is null) return;

        var items = await users.GetPortfolioAsync(user.Id, ct);
        var item = items.FirstOrDefault(x => x.Id == itemId);
        if (item is null) return;

        var chatId = callback.Message!.Chat.Id;

        var keyboard = new InlineKeyboardMarkup(new[]
        {
        new[] { InlineKeyboardButton.WithCallbackData("🗑 Удалить", $"pfl_del:{item.Id}") },
        new[] { InlineKeyboardButton.WithCallbackData("« К портфолио", "portfolio") }
    });

        await bot.SendPhoto(
            chatId: chatId,
            photo: Telegram.Bot.Types.InputFile.FromFileId(item.TelegramFileId),
            caption: string.IsNullOrEmpty(item.Caption) ? "Без подписи" : item.Caption,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task HandlePortfolioDeleteAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        if (!long.TryParse(callback.Data!.Replace("pfl_del:", ""), out var itemId)) return;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(callback.From.Id, ct);
        if (user is null) return;

        var ok = await users.DeletePortfolioAsync(user.Id, itemId, ct);
        var chatId = callback.Message!.Chat.Id;

        await bot.SendMessage(chatId,
            ok ? "🗑 Работа удалена из портфолио." : "Не удалось удалить.",
            cancellationToken: ct);

        await SendPortfolioAsync(bot, chatId, callback.From.Id, ct);
    }

    private async Task StartAddPortfolioFsmAsync(
        ITelegramBotClient bot, long chatId, long telegramId, CancellationToken ct)
    {
        var state = new UserState { CurrentStep = "pfl_photo" };
        await _states.SetAsync(telegramId, state);

        await bot.SendMessage(chatId,
            "🖼 Добавление работы в портфолио\n\n" +
            "Шаг 1 из 2: отправьте фото работы.\n\n" +
            "Можно прикрепить через скрепку 📎 как изображение " +
            "или как файл (тогда сохранится в оригинальном качестве).",
            cancellationToken: ct);
    }

    private async Task HandlePhotoUploadAsync(
        ITelegramBotClient bot, Message message, string telegramFileId, CancellationToken ct)
    {
        if (message.From is null) return;

        var state = await _states.GetAsync(message.From.Id);

        // Если пользователь не в FSM добавления портфолио — просто игнорируем фото
        if (state?.CurrentStep != "pfl_photo")
        {
            await bot.SendMessage(message.Chat.Id,
                "Чтобы добавить фото в портфолио, зайдите в:\n" +
                "📊 Мой кабинет → 🖼 Моё портфолио → ➕ Добавить фото",
                cancellationToken: ct);
            return;
        }

        state.Data["file_id"] = telegramFileId;
        state.CurrentStep = "pfl_caption";
        await _states.SetAsync(message.From.Id, state);

        await bot.SendMessage(message.Chat.Id,
            "Принял фото 📷\n\n" +
            "Шаг 2 из 2: добавьте подпись (необязательно).\n" +
            "Например: «Французский маникюр» или «Стрижка градиент».\n\n" +
            "Или отправьте «-» чтобы пропустить.",
            cancellationToken: ct);
    }

    private async Task HandlePortfolioCaptionAsync(
        ITelegramBotClient bot, Message message, UserState state, CancellationToken ct)
    {
        var caption = (message.Text ?? "").Trim();
        if (caption == "-" || caption.ToLower() == "пропустить") caption = "";

        if (caption.Length > 500)
        {
            await bot.SendMessage(message.Chat.Id,
                "Подпись слишком длинная (макс 500 символов). Попробуйте короче.",
                cancellationToken: ct);
            return;
        }

        if (!state.Data.TryGetValue("file_id", out var fileId) || string.IsNullOrEmpty(fileId))
        {
            await bot.SendMessage(message.Chat.Id,
                "Не нашёл фото. Начните заново через ➕ Добавить фото.",
                cancellationToken: ct);
            await _states.ClearAsync(message.From!.Id);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(message.From!.Id, ct);
        if (user is null) return;

        var item = await users.AddPortfolioAsync(
            user.Id, fileId, string.IsNullOrEmpty(caption) ? null : caption, ct);
        await _states.ClearAsync(message.From.Id);

        _logger.LogInformation(
            "Добавлено фото в портфолио: user={UserId}, item={ItemId}",
            user.Id, item.Id);

        await bot.SendMessage(message.Chat.Id,
            $"✅ Работа добавлена в портфолио!" +
            (string.IsNullOrEmpty(caption) ? "" : $"\n\n📝 Подпись: {caption}"),
            cancellationToken: ct);

        await SendPortfolioAsync(bot, message.Chat.Id, message.From.Id, ct);
    }

    private static string GetWorksWord(int count)
    {
        int n = count % 100;
        if (n >= 11 && n <= 19) return "работ";
        return (n % 10) switch
        {
            1 => "работа",
            2 or 3 or 4 => "работы",
            _ => "работ"
        };
    }

    private async Task SendMasterBookingsAsync(
    ITelegramBotClient bot, long chatId, long telegramId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(telegramId, ct);
        if (user is null) return;

        var bookings = await users.GetMasterBookingsAsync(user.Id, ct);

        if (bookings.Count == 0)
        {
            await bot.SendMessage(chatId,
                "📒 Мои записи\n\nПока ни одной активной записи.",
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                new[] { InlineKeyboardButton.WithCallbackData("« Назад в кабинет", "cabinet") }
                }),
                cancellationToken: ct);
            return;
        }

        var allServices = await users.GetServicesAsync(user.Id, ct);
        var masterTz = user.TimeZone;
        var nowUtc = DateTime.UtcNow;

        var text = $"📒 Активные записи: {bookings.Count}\n\n" +
            string.Join("\n\n", bookings.Select(b =>
            {
                var clientName = !string.IsNullOrEmpty(b.ClientFirstName)
                    ? b.ClientFirstName
                    : "Клиент";
                var clientUsername = !string.IsNullOrEmpty(b.ClientUsername)
                    ? $" (@{b.ClientUsername})"
                    : "";
                var startLocal = ClientiX.Infrastructure.TimeZones.ToZone(b.StartsAt, masterTz);
                var endLocal = ClientiX.Infrastructure.TimeZones.ToZone(b.EndsAt, masterTz);
                var servicesList = FormatBookingServicesList(b, allServices);
                var passedMark = b.EndsAt < nowUtc ? "⏳ ПРОШЛА — отметьте статус\n" : "";
                return
                    passedMark +
                    $"📅 {startLocal:dd.MM} в {startLocal:HH:mm}–{endLocal:HH:mm}\n" +
                    $"💼 {servicesList}, {b.PriceRub} руб.\n" +
                    $"👤 {clientName}{clientUsername}";
            }));

        text += "\n\nНажмите на запись ниже, чтобы отменить.";

        var buttons = new List<InlineKeyboardButton[]>();
        

        foreach (var b in bookings.Take(10))
        {
            var startLocal = ClientiX.Infrastructure.TimeZones.ToZone(b.StartsAt, masterTz);
            bool isPast = b.EndsAt < nowUtc;

            int servicesCount = 1;
            if (!string.IsNullOrEmpty(b.AdditionalServiceIds))
                servicesCount += b.AdditionalServiceIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Length;

            if (isPast)
            {
                // Прошедшая запись — кнопки для завершения
                buttons.Add(new[]
                {
            InlineKeyboardButton.WithCallbackData(
                $"✅ Выполнено {startLocal:dd.MM HH:mm}",
                $"master_complete:{b.Id}")
        });
                buttons.Add(new[]
                {
            InlineKeyboardButton.WithCallbackData(
                $"🕳 Не пришёл {startLocal:dd.MM HH:mm}",
                $"master_noshow:{b.Id}")
        });
            }
            else
            {
                // Активная запись — управление
                buttons.Add(new[]
                {
            InlineKeyboardButton.WithCallbackData(
                $"➕ Доп. услуга {startLocal:dd.MM HH:mm}",
                $"master_add_service:{b.Id}")
        });

                if (servicesCount > 1)
                {
                    buttons.Add(new[]
                    {
                InlineKeyboardButton.WithCallbackData(
                    $"➖ Убрать услугу {startLocal:dd.MM HH:mm}",
                    $"master_remove_service:{b.Id}")
            });
                }

                buttons.Add(new[]
                {
            InlineKeyboardButton.WithCallbackData(
                $"🔄 Перенести {startLocal:dd.MM HH:mm}",
                $"master_reschedule:{b.Id}")
        });

                buttons.Add(new[]
                {
            InlineKeyboardButton.WithCallbackData(
                $"🚫 Отменить {startLocal:dd.MM HH:mm}",
                $"master_cancel_booking:{b.Id}")
        });
            }
        }
    }

    private async Task HandleMasterCancelBookingAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        if (!long.TryParse(callback.Data!.Replace("master_cancel_booking:", ""), out var bookingId))
            return;

        var chatId = callback.Message!.Chat.Id;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();

        var user = await users.GetByTelegramIdAsync(callback.From.Id, ct);
        if (user is null) return;

        var booking = await users.GetBookingByIdAsync(bookingId, ct);
        if (booking is null || booking.UserId != user.Id)
        {
            await bot.SendMessage(chatId, "Запись не найдена.", cancellationToken: ct);
            return;
        }

        await users.CancelBookingAsync(bookingId, "master", null, ct);

        await bot.SendMessage(chatId,
            $"🚫 Запись отменена:\n📅 {booking.StartsAt:dd.MM в HH:mm}\n💼 {booking.Service.Name}",
            cancellationToken: ct);

        _logger.LogInformation(
            "Мастер отменил запись id={BookingId}, master={MasterId}",
            booking.Id, user.Id);

        // Уведомление клиенту через бот мастера
        await NotifyClientAboutCancellationAsync(scope, user.Id, booking, ct);

        await SendMasterBookingsAsync(bot, chatId, callback.From.Id, ct);
    }

    private async Task NotifyClientAboutCancellationAsync(
        IServiceScope scope, long masterUserId,
        Domain.Entities.Booking booking, CancellationToken ct)
    {
        // Получаем менеджер ботов мастеров и берём клиент бота мастера
        var manager = scope.ServiceProvider
            .GetRequiredService<ClientiX.BotGateway.MasterBots.MasterBotManager>();

        if (!manager.ActiveBots.TryGetValue(masterUserId, out var masterBotContext))
        {
            _logger.LogWarning(
                "Бот мастера {MasterId} не запущен, не могу уведомить клиента", masterUserId);
            return;
        }

        var text = "❌ К сожалению, мастер отменил вашу запись:\n\n" +
                   $"📅 {booking.StartsAt:dddd, dd MMMM} в {booking.StartsAt:HH:mm}\n" +
                   $"💼 {booking.Service.Name}\n\n" +
                   "Свяжитесь с мастером для уточнения или выберите другое время.";

        try
        {
            await masterBotContext.Client.SendMessage(
                chatId: booking.ClientTelegramId,
                text: text,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Не удалось уведомить клиента {ClientTgId} об отмене",
                booking.ClientTelegramId);
        }
    }

    private async Task SendTimezonePickerAsync(
    ITelegramBotClient bot, long chatId, long telegramId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(telegramId, ct);
        if (user is null) return;

        var current = user.TimeZone;

        var text = "🌍 Часовой пояс\n\n" +
                   $"Сейчас: <b>{GetTimezoneLabel(current)}</b>\n\n" +
                   "Выберите ваш часовой пояс.\n" +
                   "От него зависит расписание и время записей клиентов.";

        var buttons = ClientiX.Infrastructure.TimeZones.RussianZones
            .Select(z => new[] {
            InlineKeyboardButton.WithCallbackData(
                z.Id == current ? $"✅ {z.Label}" : z.Label,
                $"tz_set:{z.Id}")
            })
            .ToList();

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("« Назад в кабинет", "cabinet") });

        await bot.SendMessage(chatId, text,
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task HandleTimezoneSetAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var tzId = callback.Data!.Replace("tz_set:", "");
        var validIds = ClientiX.Infrastructure.TimeZones.RussianZones.Select(z => z.Id).ToHashSet();
        if (!validIds.Contains(tzId)) return;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        await users.UpdateTimezoneAsync(callback.From.Id, tzId, ct);

        var chatId = callback.Message!.Chat.Id;
        await bot.SendMessage(chatId,
            $"✅ Часовой пояс изменён на: {GetTimezoneLabel(tzId)}",
            cancellationToken: ct);

        await SendTimezonePickerAsync(bot, chatId, callback.From.Id, ct);
    }

    private static string GetTimezoneLabel(string id) =>
        ClientiX.Infrastructure.TimeZones.RussianZones
            .FirstOrDefault(z => z.Id == id).Label ?? id;

    private async Task SendHorizonPickerAsync(
    ITelegramBotClient bot, long chatId, long telegramId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(telegramId, ct);
        if (user is null) return;

        var current = user.BookingHorizonDays;
        var text = "⏰ Горизонт записи\n\n" +
                   $"Сейчас: <b>{current} дней</b>\n\n" +
                   "На сколько дней вперёд клиент может видеть свободные слоты и записываться?\n\n" +
                   "▪️ 7 дней — для активной практики (близкая запись)\n" +
                   "▪️ 14 дней — стандарт индустрии\n" +
                   "▪️ 30 дней — для постоянных клиентов с планированием\n" +
                   "▪️ 60 дней — для услуг с долгим циклом";

        int[] options = { 7, 14, 30, 60 };
        var buttons = options.Select(d => new[] {
        InlineKeyboardButton.WithCallbackData(
            d == current ? $"✅ {d} дней" : $"{d} дней",
            $"horizon_set:{d}")
    }).ToList();
        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("« Назад в кабинет", "cabinet") });

        await bot.SendMessage(chatId, text,
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task HandleHorizonSetAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
        if (!int.TryParse(callback.Data!.Replace("horizon_set:", ""), out var days)) return;
        if (days != 7 && days != 14 && days != 30 && days != 60) return;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        await users.UpdateBookingHorizonAsync(callback.From.Id, days, ct);

        var chatId = callback.Message!.Chat.Id;
        await bot.SendMessage(chatId,
            $"✅ Горизонт записи обновлён: {days} дней",
            cancellationToken: ct);
        await SendHorizonPickerAsync(bot, chatId, callback.From.Id, ct);
    }

    // ============================================================
    // КАЛЕНДАРЬ РАСПИСАНИЯ МАСТЕРА
    // ============================================================

    private async Task SendCalendarAsync(
        ITelegramBotClient bot, long chatId, long telegramId, DateTime monthAnchor, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(telegramId, ct);
        if (user is null) return;

        var tz = user.TimeZone;
        var nowLocal = ClientiX.Infrastructure.TimeZones.NowInZone(tz);

        var anchorLocal = ClientiX.Infrastructure.TimeZones.ToZone(monthAnchor, tz);
        var firstOfMonth = new DateTime(anchorLocal.Year, anchorLocal.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var schedule = await users.GetWeeklyScheduleAsync(user.Id, ct);
        var exceptionsAll = await _GetExceptionsForMonthAsync(scope, user.Id, firstOfMonth, ct);
        var exceptions = exceptionsAll.ToDictionary(e => e.Date.Date, e => e);

        int daysInMonth = DateTime.DaysInMonth(firstOfMonth.Year, firstOfMonth.Month);
        int firstWeekday = ((int)firstOfMonth.DayOfWeek + 6) % 7; // понедельник = 0

        var ruMonths = new[] { "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь",
                       "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь" };
        var monthName = $"{ruMonths[firstOfMonth.Month - 1]} {firstOfMonth.Year}";

        var text = "📅 Расписание мастера\n\n" +
                   $"<b>{monthName}</b>\n\n" +
                   "🟢 рабочий день  🔴 выходной  🟡 особый  ⚪ прошедший";

        var buttons = new List<InlineKeyboardButton[]>();

        var prevMonth = firstOfMonth.AddMonths(-1);
        var nextMonth = firstOfMonth.AddMonths(1);

        // Ограничиваем навигацию: назад — только до текущего месяца, вперёд — до конца горизонта
        var currentMonthFirst = new DateTime(nowLocal.Year, nowLocal.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var horizonEnd = nowLocal.Date.AddDays(user.BookingHorizonDays);
        var horizonMonthFirst = new DateTime(horizonEnd.Year, horizonEnd.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var navPrev = prevMonth >= currentMonthFirst
            ? InlineKeyboardButton.WithCallbackData("«", $"cal_nav:{prevMonth:yyyy-MM}")
            : InlineKeyboardButton.WithCallbackData(" ", "noop");

        var navNext = nextMonth <= horizonMonthFirst
            ? InlineKeyboardButton.WithCallbackData("»", $"cal_nav:{nextMonth:yyyy-MM}")
            : InlineKeyboardButton.WithCallbackData(" ", "noop");

        buttons.Add(new[]
        {
    navPrev,
    InlineKeyboardButton.WithCallbackData(monthName, "noop"),
    navNext
});

        buttons.Add(new[]
        {
        InlineKeyboardButton.WithCallbackData("Пн", "noop"),
        InlineKeyboardButton.WithCallbackData("Вт", "noop"),
        InlineKeyboardButton.WithCallbackData("Ср", "noop"),
        InlineKeyboardButton.WithCallbackData("Чт", "noop"),
        InlineKeyboardButton.WithCallbackData("Пт", "noop"),
        InlineKeyboardButton.WithCallbackData("Сб", "noop"),
        InlineKeyboardButton.WithCallbackData("Вс", "noop"),
    });

        var row = new List<InlineKeyboardButton>();
        for (int i = 0; i < firstWeekday; i++)
            row.Add(InlineKeyboardButton.WithCallbackData(" ", "noop"));

        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateTime(firstOfMonth.Year, firstOfMonth.Month, day, 0, 0, 0, DateTimeKind.Unspecified);
            string emoji;

            if (date.Date < nowLocal.Date)
            {
                emoji = "⚪";
            }
            else if (exceptions.TryGetValue(date.Date, out var exc))
            {
                emoji = exc.IsWorking ? "🟡" : "🔴";
            }
            else
            {
                var dow = (int)date.DayOfWeek;
                var template = schedule.FirstOrDefault(s => s.DayOfWeek == dow);
                emoji = (template?.IsWorking ?? false) ? "🟢" : "🔴";
            }

            // Прошедшие даты в текущем месяце не кликабельны
            var callback_ = date.Date < nowLocal.Date ? "noop" : $"cal_day:{date:yyyy-MM-dd}";
            row.Add(InlineKeyboardButton.WithCallbackData($"{emoji}{day}", callback_));

            if (row.Count == 7)
            {
                buttons.Add(row.ToArray());
                row = new List<InlineKeyboardButton>();
            }
        }

        if (row.Count > 0)
        {
            while (row.Count < 7) row.Add(InlineKeyboardButton.WithCallbackData(" ", "noop"));
            buttons.Add(row.ToArray());
        }

        buttons.Add(new[] {
        InlineKeyboardButton.WithCallbackData("⚙️ Шаблон недели", "schedule_week_template")
    });
        buttons.Add(new[] {
        InlineKeyboardButton.WithCallbackData("« Назад в кабинет", "cabinet")
    });

        await bot.SendMessage(chatId, text,
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task HandleCalendarNavAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var ymStr = callback.Data!.Replace("cal_nav:", "");
        if (!DateTime.TryParseExact(ymStr + "-01", "yyyy-MM-dd", null,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var newMonth))
            return;

        await SendCalendarAsync(bot, callback.Message!.Chat.Id, callback.From.Id, newMonth.ToUniversalTime(), ct);
    }

    private async Task HandleCalendarDayClickAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var dateStr = callback.Data!.Replace("cal_day:", "");
        if (!DateTime.TryParseExact(dateStr, "yyyy-MM-dd", null,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var date))
            return;

        date = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(callback.From.Id, ct);
        if (user is null) return;

        var nowLocal = ClientiX.Infrastructure.TimeZones.NowInZone(user.TimeZone);
        if (date.Date < nowLocal.Date)
        {
            await bot.SendMessage(callback.Message!.Chat.Id,
                "⚪ Эта дата уже в прошлом — изменить её нельзя.",
                cancellationToken: ct);
            return;
        }

        var dateAsUtc = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        var exceptions = await users.GetUpcomingExceptionsAsync(user.Id, ct);
        var exc = exceptions.FirstOrDefault(e => e.Date.Date == dateAsUtc.Date);

        var schedule = await users.GetWeeklyScheduleAsync(user.Id, ct);
        var template = schedule.First(s => s.DayOfWeek == (int)date.DayOfWeek);

        string statusText;
        if (exc is not null)
        {
            statusText = exc.IsWorking
                ? $"🟡 Особый день: {FormatTime(exc.FromMinutes)}–{FormatTime(exc.ToMinutes)}"
                  + (string.IsNullOrEmpty(exc.Note) ? "" : $"\n💬 {exc.Note}")
                : "🔴 Выходной"
                  + (string.IsNullOrEmpty(exc.Note) ? "" : $"\n💬 {exc.Note}");
        }
        else
        {
            statusText = template.IsWorking
                ? $"🟢 Рабочий день (по шаблону): {FormatTime(template.FromMinutes)}–{FormatTime(template.ToMinutes)}"
                : "🔴 Выходной (по шаблону)";
        }

        var text = $"📅 {date:dddd, dd MMMM yyyy}\n\n{statusText}";

        var buttons = new List<InlineKeyboardButton[]>
    {
        new[] { InlineKeyboardButton.WithCallbackData("🟢 Рабочий день", $"day_set:{date:yyyy-MM-dd}:work") },
        new[] { InlineKeyboardButton.WithCallbackData("🔴 Выходной", $"day_set:{date:yyyy-MM-dd}:off") },
        new[] { InlineKeyboardButton.WithCallbackData("🟡 Особые часы", $"day_set:{date:yyyy-MM-dd}:hours") }
    };

        if (exc is not null)
        {
            buttons.Add(new[] {
            InlineKeyboardButton.WithCallbackData("↺ Сбросить (использовать шаблон)",
                $"day_set:{date:yyyy-MM-dd}:reset")
        });
        }

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("« К календарю", "schedule") });

        await bot.SendMessage(callback.Message!.Chat.Id, text,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task HandleDaySetAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var parts = callback.Data!.Replace("day_set:", "").Split(':');
        if (parts.Length != 2) return;
        if (!DateTime.TryParseExact(parts[0], "yyyy-MM-dd", null,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var date)) return;

        date = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
        var action = parts[1];
        var chatId = callback.Message!.Chat.Id;
        var tgId = callback.From.Id;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(tgId, ct);
        if (user is null) return;

        if (action == "work")
        {
            var dow = (int)date.DayOfWeek;
            var schedule = await users.GetWeeklyScheduleAsync(user.Id, ct);
            var template = schedule.First(s => s.DayOfWeek == dow);

            int from = template.IsWorking ? template.FromMinutes : 600;
            int to = template.IsWorking ? template.ToMinutes : 1200;

            await users.UpsertExceptionAsync(user.Id, date, true, from, to, null, ct);
            await bot.SendMessage(chatId,
                $"✅ {date:dd MMMM}: рабочий день {FormatTime(from)}–{FormatTime(to)}",
                cancellationToken: ct);
        }
        else if (action == "off")
        {
            await users.UpsertExceptionAsync(user.Id, date, false, 0, 0, null, ct);
            await bot.SendMessage(chatId,
                $"🔴 {date:dd MMMM}: выходной",
                cancellationToken: ct);
        }
        else if (action == "hours")
        {
            var state = new UserState
            {
                CurrentStep = "day_set_hours",
                Data = { ["date"] = date.ToString("O") }
            };
            await _states.SetAsync(tgId, state);

            await bot.SendMessage(chatId,
                $"🟡 {date:dd MMMM} — особые часы\n\n" +
                "Введите часы в формате <code>ЧЧ:ММ-ЧЧ:ММ</code>\n" +
                "Например: <code>14:00-18:00</code>",
                parseMode: ParseMode.Html,
                cancellationToken: ct);
            return;
        }
        else if (action == "reset")
        {
            var dateAsUtc = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
            var existing = await users.GetUpcomingExceptionsAsync(user.Id, ct);
            var exc = existing.FirstOrDefault(e => e.Date.Date == dateAsUtc.Date);
            if (exc is not null)
            {
                await users.DeleteExceptionAsync(user.Id, exc.Id, ct);
                await bot.SendMessage(chatId,
                    $"↺ {date:dd MMMM}: теперь действует шаблон недели",
                    cancellationToken: ct);
            }
        }

        var anchor = DateTime.SpecifyKind(
            new DateTime(date.Year, date.Month, 1, 0, 0, 0), DateTimeKind.Utc);
        await SendCalendarAsync(bot, chatId, tgId, anchor, ct);
    }

    private async Task HandleDaySetHoursAsync(
        ITelegramBotClient bot, Message message, UserState state, CancellationToken ct)
    {
        var input = (message.Text ?? "").Trim();
        var match = System.Text.RegularExpressions.Regex.Match(
            input, @"^(\d{1,2}):(\d{2})\s*[-–—]\s*(\d{1,2}):(\d{2})$");

        if (!match.Success)
        {
            await bot.SendMessage(message.Chat.Id,
                "Не понял формат. Введите как: 14:00-18:00",
                cancellationToken: ct);
            return;
        }

        int from = int.Parse(match.Groups[1].Value) * 60 + int.Parse(match.Groups[2].Value);
        int to = int.Parse(match.Groups[3].Value) * 60 + int.Parse(match.Groups[4].Value);

        if (from < 0 || to > 1439 || to <= from)
        {
            await bot.SendMessage(message.Chat.Id,
                "Время некорректно. Часы 0-23, конец позже начала.",
                cancellationToken: ct);
            return;
        }

        if (!DateTime.TryParse(state.Data.GetValueOrDefault("date", ""), null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var date)) return;

        date = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(message.From!.Id, ct);
        if (user is null) return;

        await users.UpsertExceptionAsync(user.Id, date, true, from, to, null, ct);
        await _states.ClearAsync(message.From.Id);

        await bot.SendMessage(message.Chat.Id,
            $"🟡 {date:dd MMMM}: особые часы {FormatTime(from)}–{FormatTime(to)}",
            cancellationToken: ct);

        var anchor = DateTime.SpecifyKind(
            new DateTime(date.Year, date.Month, 1, 0, 0, 0), DateTimeKind.Utc);
        await SendCalendarAsync(bot, message.Chat.Id, message.From.Id, anchor, ct);
    }

    private async Task SendWeekTemplateAsync(
        ITelegramBotClient bot, long chatId, long telegramId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(telegramId, ct);
        if (user is null) return;

        var schedule = await users.GetWeeklyScheduleAsync(user.Id, ct);
        var dayOrder = new[] { 1, 2, 3, 4, 5, 6, 0 };
        var ordered = dayOrder.Select(d => schedule.First(s => s.DayOfWeek == d)).ToList();

        var text = "⚙️ Шаблон рабочей недели\n\n" +
            string.Join("\n", ordered.Select(s =>
                s.IsWorking
                    ? $"✅ {DayNames[s.DayOfWeek]}: {FormatTime(s.FromMinutes)}–{FormatTime(s.ToMinutes)}"
                    : $"⛔ {DayNames[s.DayOfWeek]}: выходной"));

        text += "\n\nНажмите на день для изменения. Шаблон применяется ко всем датам, " +
                "если для них не задано исключение в календаре.";

        var buttons = new List<InlineKeyboardButton[]>();
        foreach (var s in ordered)
        {
            var emoji = s.IsWorking ? "✅" : "⛔";
            buttons.Add(new[] {
            InlineKeyboardButton.WithCallbackData(
                $"{emoji} {DayNames[s.DayOfWeek]}",
                $"sched_day:{s.DayOfWeek}")
        });
        }
        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("« К календарю", "schedule") });

        await bot.SendMessage(chatId, text,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task<List<WorkScheduleException>> _GetExceptionsForMonthAsync(
        IServiceScope scope, long userId, DateTime firstOfMonth, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<ClientiXDbContext>();
        var startUtc = DateTime.SpecifyKind(firstOfMonth.Date, DateTimeKind.Utc);
        var endUtc = startUtc.AddMonths(1);

        return await db.WorkScheduleExceptions
            .Where(x => x.UserId == userId && x.Date >= startUtc && x.Date < endUtc)
            .ToListAsync(ct);
    }

    private static string FormatBookingServicesList(
    Domain.Entities.Booking booking,
    List<Domain.Entities.Service> allMasterServices)
    {
        var names = new List<string> { booking.Service.Name };

        if (!string.IsNullOrEmpty(booking.AdditionalServiceIds))
        {
            var ids = booking.AdditionalServiceIds.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var idStr in ids)
            {
                if (!long.TryParse(idStr, out var svcId)) continue;
                var svc = allMasterServices.FirstOrDefault(s => s.Id == svcId);
                if (svc is not null) names.Add(svc.Name);
            }
        }

        return string.Join(" + ", names);
    }

    // ============================================================
    // МАСТЕР ДОБАВЛЯЕТ УСЛУГУ В ЗАПИСЬ КЛИЕНТА
    // ============================================================

    private async Task ShowMasterAddServiceMenuAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        if (!long.TryParse(callback.Data!.Replace("master_add_service:", ""), out var bookingId))
            return;

        var chatId = callback.Message!.Chat.Id;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var slotsService = scope.ServiceProvider
            .GetRequiredService<ClientiX.Infrastructure.Bookings.BookingSlotService>();

        var user = await users.GetByTelegramIdAsync(callback.From.Id, ct);
        if (user is null) return;

        var booking = await users.GetBookingByIdAsync(bookingId, ct);
        if (booking is null || booking.UserId != user.Id)
        {
            await bot.SendMessage(chatId, "Запись не найдена.", cancellationToken: ct);
            return;
        }

        var fittingIds = await slotsService.GetServicesFittingAfterAsync(
            user.Id, booking.EndsAt, user.TimeZone, ct);

        if (fittingIds.Count == 0)
        {
            await bot.SendMessage(chatId,
                "😔 Нет услуг, которые помещаются сразу после этой записи.",
                cancellationToken: ct);
            return;
        }

        var allServices = await users.GetServicesAsync(user.Id, ct);

        var alreadyInBooking = new HashSet<long> { booking.ServiceId };
        if (!string.IsNullOrEmpty(booking.AdditionalServiceIds))
        {
            foreach (var idStr in booking.AdditionalServiceIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (long.TryParse(idStr, out var id)) alreadyInBooking.Add(id);
            }
        }

        var available = allServices
            .Where(s => fittingIds.Contains(s.Id))
            .Where(s => !alreadyInBooking.Contains(s.Id))
            .ToList();

        if (available.Count == 0)
        {
            await bot.SendMessage(chatId,
                "Все подходящие услуги уже включены в эту запись.",
                cancellationToken: ct);
            return;
        }

        var endLocal = ClientiX.Infrastructure.TimeZones.ToZone(booking.EndsAt, user.TimeZone);

        var text = "➕ Добавление услуги клиенту\n\n" +
                   $"Запись закончится в {endLocal:HH:mm}. Доступны эти услуги:";

        var buttons = available.Select(s => new[] {
        InlineKeyboardButton.WithCallbackData(
            $"+ {s.Name} ({s.DurationMinutes} мин, {s.PriceRub} ₽)",
            $"m_add_svc:{bookingId}:{s.Id}")
    }).ToList();
        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("« К записям", "my_bookings") });

        await bot.SendMessage(chatId, text,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task HandleMasterAddServiceConfirmAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var parts = callback.Data!.Replace("m_add_svc:", "").Split(':');
        if (parts.Length != 2) return;
        if (!long.TryParse(parts[0], out var bookingId)) return;
        if (!long.TryParse(parts[1], out var serviceId)) return;

        var chatId = callback.Message!.Chat.Id;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(callback.From.Id, ct);
        if (user is null) return;

        var booking = await users.GetBookingByIdAsync(bookingId, ct);
        if (booking is null || booking.UserId != user.Id) return;

        var ok = await users.AddServiceToBookingAsync(bookingId, serviceId, ct);
        if (!ok)
        {
            await bot.SendMessage(chatId, "🚫 Не удалось добавить услугу.", cancellationToken: ct);
            return;
        }

        var bookingAfter = await users.GetBookingByIdAsync(bookingId, ct);
        if (bookingAfter is null) return;

        var allServices = await users.GetServicesAsync(user.Id, ct);
        var addedService = allServices.FirstOrDefault(s => s.Id == serviceId);
        var fullList = FormatBookingServicesList(bookingAfter, allServices);

        var endLocal = ClientiX.Infrastructure.TimeZones.ToZone(bookingAfter.EndsAt, user.TimeZone);

        await bot.SendMessage(chatId,
            $"✅ Услуга добавлена клиенту!\n\n" +
            $"➕ {addedService?.Name}\n" +
            $"💼 В записи: {fullList}\n" +
            $"⏱ Длительность: {bookingAfter.DurationMinutes} мин\n" +
            $"💰 Итого: {bookingAfter.PriceRub} ₽\n" +
            $"📅 Закончится в {endLocal:HH:mm}",
            cancellationToken: ct);

        _logger.LogInformation(
            "Мастер добавил услугу клиенту: booking={BookingId}, service={ServiceId}",
            bookingId, serviceId);

        // Уведомление клиенту через бот мастера
        await NotifyClientAboutServiceAddedByMasterAsync(scope, user.Id, bookingAfter, addedService, ct);
    }

    private async Task NotifyClientAboutServiceAddedByMasterAsync(
        IServiceScope scope, long masterUserId,
        Domain.Entities.Booking booking, Domain.Entities.Service? addedService, CancellationToken ct)
    {
        if (addedService is null) return;

        var manager = scope.ServiceProvider
            .GetRequiredService<ClientiX.BotGateway.MasterBots.MasterBotManager>();

        if (!manager.ActiveBots.TryGetValue(masterUserId, out var masterBotContext)) return;

        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var master = await users.GetByIdAsync(masterUserId, ct);
        if (master is null) return;

        var tz = master.TimeZone;
        var startLocal = ClientiX.Infrastructure.TimeZones.ToZone(booking.StartsAt, tz);
        var endLocal = ClientiX.Infrastructure.TimeZones.ToZone(booking.EndsAt, tz);
        var allServices = await users.GetServicesAsync(masterUserId, ct);
        var fullList = FormatBookingServicesList(booking, allServices);

        var text = "ℹ️ Мастер добавил услугу в вашу запись:\n\n" +
                   $"📅 {startLocal:dd.MM в HH:mm}–{endLocal:HH:mm}\n" +
                   $"➕ Добавлено: {addedService.Name}\n" +
                   $"💼 Теперь в записи: {fullList}\n" +
                   $"💰 Итого: {booking.PriceRub} ₽ ({booking.DurationMinutes} мин)";

        try
        {
            await masterBotContext.Client.SendMessage(booking.ClientTelegramId, text, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось уведомить клиента о доп. услуге от мастера");
        }
    }

    // ============================================================
    // МАСТЕР УБИРАЕТ УСЛУГУ ИЗ ЗАПИСИ КЛИЕНТА
    // ============================================================

    private async Task ShowMasterRemoveServiceMenuAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        if (!long.TryParse(callback.Data!.Replace("master_remove_service:", ""), out var bookingId))
            return;

        var chatId = callback.Message!.Chat.Id;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(callback.From.Id, ct);
        if (user is null) return;

        var booking = await users.GetBookingByIdAsync(bookingId, ct);
        if (booking is null || booking.UserId != user.Id) return;

        var allServices = await users.GetServicesAsync(user.Id, ct);

        var servicesInBooking = new List<Domain.Entities.Service>();
        var mainSvc = allServices.FirstOrDefault(s => s.Id == booking.ServiceId);
        if (mainSvc is not null) servicesInBooking.Add(mainSvc);

        if (!string.IsNullOrEmpty(booking.AdditionalServiceIds))
        {
            foreach (var idStr in booking.AdditionalServiceIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!long.TryParse(idStr, out var id)) continue;
                var svc = allServices.FirstOrDefault(s => s.Id == id);
                if (svc is not null) servicesInBooking.Add(svc);
            }
        }

        if (servicesInBooking.Count <= 1)
        {
            await bot.SendMessage(chatId,
                "В записи только одна услуга. Чтобы отменить запись — используйте 🚫 Отменить.",
                cancellationToken: ct);
            return;
        }

        var text = "➖ Какую услугу убрать у клиента?";

        var buttons = servicesInBooking.Select(s => new[] {
        InlineKeyboardButton.WithCallbackData(
            $"➖ {s.Name} ({s.DurationMinutes} мин, {s.PriceRub} ₽)",
            $"m_rm_svc:{bookingId}:{s.Id}")
    }).ToList();
        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("« К записям", "my_bookings") });

        await bot.SendMessage(chatId, text,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task HandleMasterRemoveServiceConfirmAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var parts = callback.Data!.Replace("m_rm_svc:", "").Split(':');
        if (parts.Length != 2) return;
        if (!long.TryParse(parts[0], out var bookingId)) return;
        if (!long.TryParse(parts[1], out var serviceId)) return;

        var chatId = callback.Message!.Chat.Id;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(callback.From.Id, ct);
        if (user is null) return;

        var bookingBefore = await users.GetBookingByIdAsync(bookingId, ct);
        if (bookingBefore is null || bookingBefore.UserId != user.Id) return;

        var (ok, removedName) = await users.RemoveServiceFromBookingAsync(bookingId, serviceId, ct);
        if (!ok)
        {
            await bot.SendMessage(chatId,
                "Не удалось убрать услугу. Возможно, это последняя.",
                cancellationToken: ct);
            return;
        }

        var bookingAfter = await users.GetBookingByIdAsync(bookingId, ct);
        if (bookingAfter is null) return;

        var allServices = await users.GetServicesAsync(user.Id, ct);
        var fullList = FormatBookingServicesList(bookingAfter, allServices);
        var endLocal = ClientiX.Infrastructure.TimeZones.ToZone(bookingAfter.EndsAt, user.TimeZone);

        await bot.SendMessage(chatId,
            $"✅ Услуга убрана: {removedName}\n\n" +
            $"💼 Осталось: {fullList}\n" +
            $"⏱ Длительность: {bookingAfter.DurationMinutes} мин\n" +
            $"💰 Итого: {bookingAfter.PriceRub} ₽\n" +
            $"📅 Закончится в {endLocal:HH:mm}",
            cancellationToken: ct);

        _logger.LogInformation(
            "Мастер убрал услугу из записи: booking={BookingId}, service={ServiceId}",
            bookingId, serviceId);

        // Уведомление клиенту через бот мастера
        await NotifyClientAboutServiceRemovedByMasterAsync(
            scope, user.Id, bookingAfter, removedName ?? "услугу", ct);
    }

    private async Task NotifyClientAboutServiceRemovedByMasterAsync(
        IServiceScope scope, long masterUserId,
        Domain.Entities.Booking booking, string removedServiceName, CancellationToken ct)
    {
        var manager = scope.ServiceProvider
            .GetRequiredService<ClientiX.BotGateway.MasterBots.MasterBotManager>();
        if (!manager.ActiveBots.TryGetValue(masterUserId, out var masterBotContext)) return;

        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var master = await users.GetByIdAsync(masterUserId, ct);
        if (master is null) return;

        var tz = master.TimeZone;
        var startLocal = ClientiX.Infrastructure.TimeZones.ToZone(booking.StartsAt, tz);
        var endLocal = ClientiX.Infrastructure.TimeZones.ToZone(booking.EndsAt, tz);
        var allServices = await users.GetServicesAsync(masterUserId, ct);
        var fullList = FormatBookingServicesList(booking, allServices);

        var text = "ℹ️ Мастер изменил вашу запись:\n\n" +
                   $"📅 {startLocal:dd.MM в HH:mm}–{endLocal:HH:mm}\n" +
                   $"➖ Убрано: {removedServiceName}\n" +
                   $"💼 Осталось: {fullList}\n" +
                   $"💰 Итого: {booking.PriceRub} ₽ ({booking.DurationMinutes} мин)\n\n" +
                   "Если у вас есть вопросы — свяжитесь с мастером.";

        try
        {
            await masterBotContext.Client.SendMessage(booking.ClientTelegramId, text, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось уведомить клиента об удалении услуги мастером");
        }
    }

    // ============================================================
    // МАСТЕР ПЕРЕНОСИТ ЗАПИСЬ КЛИЕНТА
    // ============================================================

    private async Task StartMasterRescheduleAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        if (!long.TryParse(callback.Data!.Replace("master_reschedule:", ""), out var bookingId))
            return;

        var chatId = callback.Message!.Chat.Id;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var slotsService = scope.ServiceProvider
            .GetRequiredService<ClientiX.Infrastructure.Bookings.BookingSlotService>();

        var user = await users.GetByTelegramIdAsync(callback.From.Id, ct);
        if (user is null) return;

        var booking = await users.GetBookingByIdAsync(bookingId, ct);
        if (booking is null || booking.UserId != user.Id)
        {
            await bot.SendMessage(chatId, "Запись не найдена.", cancellationToken: ct);
            return;
        }

        var horizon = user.BookingHorizonDays;
        var days = await slotsService.GetDaysWithAvailabilityAsync(
            user.Id, booking.DurationMinutes, horizon, DateTime.UtcNow, user.TimeZone, ct);

        if (!days.Any(d => d.HasFreeSlot))
        {
            await bot.SendMessage(chatId,
                "😔 Нет свободных слотов для переноса.",
                cancellationToken: ct);
            return;
        }

        var state = new UserState
        {
            CurrentStep = "master_rescheduling_date",
            Data =
        {
            ["booking_id"] = bookingId.ToString(),
            ["service_duration"] = booking.DurationMinutes.ToString()
        }
        };
        await _states.SetAsync(callback.From.Id, state);

        var todayLocal = ClientiX.Infrastructure.TimeZones.NowInZone(user.TimeZone).Date;
        var firstOfMonth = new DateTime(todayLocal.Year, todayLocal.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);

        await SendMasterRescheduleCalendarAsync(bot, chatId, user, booking, days, firstOfMonth, ct);
    }

    private async Task SendMasterRescheduleCalendarAsync(
        ITelegramBotClient bot, long chatId, Domain.Entities.User master,
        Domain.Entities.Booking booking,
        List<(DateTime Date, bool HasFreeSlot)> availabilityDays,
        DateTime firstOfMonth, CancellationToken ct)
    {
        var todayLocal = ClientiX.Infrastructure.TimeZones.NowInZone(master.TimeZone).Date;
        var oldLocal = ClientiX.Infrastructure.TimeZones.ToZone(booking.StartsAt, master.TimeZone);

        int daysInMonth = DateTime.DaysInMonth(firstOfMonth.Year, firstOfMonth.Month);
        int firstWeekday = ((int)firstOfMonth.DayOfWeek + 6) % 7;

        var freeMap = availabilityDays.ToDictionary(d => d.Date.Date, d => d.HasFreeSlot);

        var ruMonths = new[] { "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь",
                           "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь" };
        var monthName = $"{ruMonths[firstOfMonth.Month - 1]} {firstOfMonth.Year}";

        var clientName = !string.IsNullOrEmpty(booking.ClientFirstName) ? booking.ClientFirstName : "Клиент";

        var text = $"🔄 Перенос записи клиента\n\n" +
                   $"👤 {clientName}\n" +
                   $"Текущая: <b>{oldLocal:dd.MM в HH:mm}</b>\n\n" +
                   $"<b>{monthName}</b>\n\n" +
                   $"Выберите новую дату:";

        var buttons = new List<InlineKeyboardButton[]>();
        buttons.Add(new[]
        {
        InlineKeyboardButton.WithCallbackData("Пн", "noop"),
        InlineKeyboardButton.WithCallbackData("Вт", "noop"),
        InlineKeyboardButton.WithCallbackData("Ср", "noop"),
        InlineKeyboardButton.WithCallbackData("Чт", "noop"),
        InlineKeyboardButton.WithCallbackData("Пт", "noop"),
        InlineKeyboardButton.WithCallbackData("Сб", "noop"),
        InlineKeyboardButton.WithCallbackData("Вс", "noop"),
    });

        var row = new List<InlineKeyboardButton>();
        for (int i = 0; i < firstWeekday; i++)
            row.Add(InlineKeyboardButton.WithCallbackData(" ", "noop"));

        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateTime(firstOfMonth.Year, firstOfMonth.Month, day, 0, 0, 0, DateTimeKind.Unspecified);
            bool hasFree = freeMap.TryGetValue(date.Date, out var f) && f;
            bool isPast = date.Date < todayLocal;

            if (isPast || !hasFree)
                row.Add(InlineKeyboardButton.WithCallbackData($"⚪{day}", "noop"));
            else
                row.Add(InlineKeyboardButton.WithCallbackData($"🟢{day}", $"m_resched_date:{date:yyyy-MM-dd}"));

            if (row.Count == 7) { buttons.Add(row.ToArray()); row = new List<InlineKeyboardButton>(); }
        }
        if (row.Count > 0)
        {
            while (row.Count < 7) row.Add(InlineKeyboardButton.WithCallbackData(" ", "noop"));
            buttons.Add(row.ToArray());
        }

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("« К записям", "my_bookings") });

        await bot.SendMessage(chatId, text,
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task HandleMasterRescheduleDateAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var dateStr = callback.Data!.Replace("m_resched_date:", "");
        if (!DateTime.TryParseExact(dateStr, "yyyy-MM-dd", null,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var date))
            return;
        date = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);

        var chatId = callback.Message!.Chat.Id;
        var tgId = callback.From.Id;

        var state = await _states.GetAsync(tgId);
        if (state is null || state.CurrentStep != "master_rescheduling_date") return;

        if (!int.TryParse(state.Data.GetValueOrDefault("service_duration", "0"), out var duration)) return;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(tgId, ct);
        if (user is null) return;

        var slotsService = scope.ServiceProvider
            .GetRequiredService<ClientiX.Infrastructure.Bookings.BookingSlotService>();

        var slots = await slotsService.GetAvailableSlotsAsync(
            user.Id, duration, date, DateTime.UtcNow, user.TimeZone, ct);

        if (slots.Count == 0)
        {
            await bot.SendMessage(chatId,
                "На эту дату слотов нет. Выберите другую.",
                cancellationToken: ct);
            return;
        }

        state.CurrentStep = "master_rescheduling_slot";
        state.Data["new_date"] = date.ToString("O");
        await _states.SetAsync(tgId, state);

        var text = $"📅 {date:dddd, dd MMMM}\n\nВыберите новое время:";

        var buttons = new List<InlineKeyboardButton[]>();
        var row = new List<InlineKeyboardButton>();
        foreach (var slot in slots)
        {
            var slotLocal = ClientiX.Infrastructure.TimeZones.ToZone(slot, user.TimeZone);
            row.Add(InlineKeyboardButton.WithCallbackData(
                slotLocal.ToString("HH:mm"),
                $"m_resched_slot:{slot:yyyy-MM-ddTHH:mm}"));
            if (row.Count == 3) { buttons.Add(row.ToArray()); row = new List<InlineKeyboardButton>(); }
        }
        if (row.Count > 0) buttons.Add(row.ToArray());
        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("« К записям", "my_bookings") });

        await bot.SendMessage(chatId, text,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task HandleMasterRescheduleSlotAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var slotStr = callback.Data!.Replace("m_resched_slot:", "");
        if (!DateTime.TryParseExact(slotStr, "yyyy-MM-ddTHH:mm", null,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var slot)) return;
        slot = DateTime.SpecifyKind(slot, DateTimeKind.Utc);

        var chatId = callback.Message!.Chat.Id;
        var tgId = callback.From.Id;

        var state = await _states.GetAsync(tgId);
        if (state is null) return;

        state.CurrentStep = "master_rescheduling_confirm";
        state.Data["new_slot"] = slot.ToString("O");
        await _states.SetAsync(tgId, state);

        if (!long.TryParse(state.Data.GetValueOrDefault("booking_id", "0"), out var bookingId)) return;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(tgId, ct);
        if (user is null) return;

        var booking = await users.GetBookingByIdAsync(bookingId, ct);
        if (booking is null) return;

        var oldLocal = ClientiX.Infrastructure.TimeZones.ToZone(booking.StartsAt, user.TimeZone);
        var newLocal = ClientiX.Infrastructure.TimeZones.ToZone(slot, user.TimeZone);

        var clientName = !string.IsNullOrEmpty(booking.ClientFirstName) ? booking.ClientFirstName : "Клиент";

        var text = "🔄 Подтвердите перенос:\n\n" +
                   $"👤 {clientName}\n" +
                   $"📅 Было: {oldLocal:dd.MM в HH:mm}\n" +
                   $"📅 Станет: <b>{newLocal:dd.MM в HH:mm}</b>\n\n" +
                   "Подтвердить?";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
        new[] { InlineKeyboardButton.WithCallbackData("✅ Перенести", "m_resched_confirm") },
        new[] { InlineKeyboardButton.WithCallbackData("« К записям", "my_bookings") }
    });

        await bot.SendMessage(chatId, text,
            parseMode: ParseMode.Html,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task HandleMasterRescheduleConfirmAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var chatId = callback.Message!.Chat.Id;
        var tgId = callback.From.Id;

        var state = await _states.GetAsync(tgId);
        if (state is null || state.CurrentStep != "master_rescheduling_confirm")
        {
            await bot.SendMessage(chatId, "Сессия устарела.", cancellationToken: ct);
            return;
        }

        if (!long.TryParse(state.Data.GetValueOrDefault("booking_id", "0"), out var bookingId)) return;
        if (!DateTime.TryParse(state.Data.GetValueOrDefault("new_slot", ""), null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var newSlot)) return;
        newSlot = DateTime.SpecifyKind(newSlot, DateTimeKind.Utc);

        if (!int.TryParse(state.Data.GetValueOrDefault("service_duration", "0"), out var duration)) return;
        var newEnd = newSlot.AddMinutes(duration);

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(tgId, ct);
        if (user is null) return;

        var booking = await users.GetBookingByIdAsync(bookingId, ct);
        if (booking is null || booking.UserId != user.Id) return;
        var oldStart = booking.StartsAt;

        var ok = await users.RescheduleBookingAsync(bookingId, newSlot, newEnd, ct);
        if (!ok)
        {
            await bot.SendMessage(chatId,
                "🚫 Это время уже занято другой записью.",
                cancellationToken: ct);
            return;
        }

        await _states.ClearAsync(tgId);

        var newLocal = ClientiX.Infrastructure.TimeZones.ToZone(newSlot, user.TimeZone);

        await bot.SendMessage(chatId,
            $"✅ Запись перенесена!\n\n📅 Новое время: {newLocal:dddd, dd MMMM в HH:mm}",
            cancellationToken: ct);

        _logger.LogInformation(
            "Мастер перенёс запись id={BookingId}: {Old} → {New}",
            bookingId, oldStart, newSlot);

        // Уведомление клиенту через бот мастера
        await NotifyClientAboutRescheduleByMasterAsync(scope, user.Id, booking, oldStart, newSlot, ct);
    }

    private async Task NotifyClientAboutRescheduleByMasterAsync(
        IServiceScope scope, long masterUserId,
        Domain.Entities.Booking booking, DateTime oldStart, DateTime newStart, CancellationToken ct)
    {
        var manager = scope.ServiceProvider
            .GetRequiredService<ClientiX.BotGateway.MasterBots.MasterBotManager>();
        if (!manager.ActiveBots.TryGetValue(masterUserId, out var masterBotContext)) return;

        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var master = await users.GetByIdAsync(masterUserId, ct);
        if (master is null) return;

        var tz = master.TimeZone;
        var oldLocal = ClientiX.Infrastructure.TimeZones.ToZone(oldStart, tz);
        var newLocal = ClientiX.Infrastructure.TimeZones.ToZone(newStart, tz);
        var allServices = await users.GetServicesAsync(masterUserId, ct);
        var fullList = FormatBookingServicesList(booking, allServices);

        var text = "🔄 Мастер перенёс вашу запись:\n\n" +
                   $"💼 {fullList}\n" +
                   $"📅 Было: {oldLocal:dd.MM в HH:mm}\n" +
                   $"📅 Стало: <b>{newLocal:dd.MM в HH:mm}</b>\n\n" +
                   "Если время не подходит — свяжитесь с мастером или отмените запись.";

        try
        {
            await masterBotContext.Client.SendMessage(
                booking.ClientTelegramId, text,
                parseMode: ParseMode.Html, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось уведомить клиента о переносе мастером");
        }
    }

    // ============================================================
    // НАПОМИНАНИЯ КЛИЕНТАМ — НАСТРОЙКИ МАСТЕРА
    // ============================================================

    private async Task SendRemindersMenuAsync(
        ITelegramBotClient bot, long chatId, long telegramId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(telegramId, ct);
        if (user is null) return;

        var dayStatus = user.ReminderDayBefore ? "✅ Включено" : "⛔ Выключено";
        var extraText = user.ReminderExtraHours.HasValue
            ? $"✅ За {user.ReminderExtraHours} часов до записи"
            : "⛔ Выключено";

        var text = "🔔 Напоминания клиентам\n\n" +
                   $"<b>За 24 часа:</b> {dayStatus}\n" +
                   $"<b>Дополнительно:</b> {extraText}\n\n" +
                   "Напоминания приходят клиенту через ваш бот.";

        var buttons = new List<InlineKeyboardButton[]>
    {
        new[] {
            InlineKeyboardButton.WithCallbackData(
                user.ReminderDayBefore ? "⛔ Выключить за 24 ч" : "✅ Включить за 24 ч",
                $"rem_24:{(user.ReminderDayBefore ? "off" : "on")}")
        },
    };

        int[] extraOptions = { 1, 3, 6, 12 };
        foreach (var h in extraOptions)
        {
            var label = user.ReminderExtraHours == h ? $"✅ За {h} ч" : $"За {h} ч";
            buttons.Add(new[] {
            InlineKeyboardButton.WithCallbackData(label, $"rem_extra:{h}")
        });
        }
        buttons.Add(new[] {
        InlineKeyboardButton.WithCallbackData(
            user.ReminderExtraHours.HasValue ? "⛔ Выключить дополнительное" : "Дополнительное выключено",
            "rem_extra:off")
    });

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("« Назад в кабинет", "cabinet") });

        await bot.SendMessage(chatId, text,
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task HandleReminder24SetAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var action = callback.Data!.Replace("rem_24:", "");
        bool enable = action == "on";

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        await users.UpdateReminderDayBeforeAsync(callback.From.Id, enable, ct);

        await SendRemindersMenuAsync(bot, callback.Message!.Chat.Id, callback.From.Id, ct);
    }

    private async Task HandleReminderExtraSetAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var raw = callback.Data!.Replace("rem_extra:", "");
        int? hours = raw == "off" ? null : (int.TryParse(raw, out var h) ? h : null);

        if (hours.HasValue && hours.Value != 1 && hours.Value != 3 && hours.Value != 6 && hours.Value != 12) return;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        await users.UpdateReminderExtraHoursAsync(callback.From.Id, hours, ct);

        await SendRemindersMenuAsync(bot, callback.Message!.Chat.Id, callback.From.Id, ct);
    }

    private async Task HandleMasterCompleteBookingAsync(
    ITelegramBotClient bot, CallbackQuery callback, string newStatus, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var prefix = newStatus == "completed" ? "master_complete:" : "master_noshow:";
        if (!long.TryParse(callback.Data!.Replace(prefix, ""), out var bookingId)) return;

        var chatId = callback.Message!.Chat.Id;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var user = await users.GetByTelegramIdAsync(callback.From.Id, ct);
        if (user is null) return;

        var booking = await users.GetBookingByIdAsync(bookingId, ct);
        if (booking is null || booking.UserId != user.Id)
        {
            await bot.SendMessage(chatId, "Запись не найдена.", cancellationToken: ct);
            return;
        }

        var ok = await users.CompleteBookingAsync(bookingId, newStatus, ct);
        if (!ok)
        {
            await bot.SendMessage(chatId, "Не удалось изменить статус.", cancellationToken: ct);
            return;
        }

        var startLocal = ClientiX.Infrastructure.TimeZones.ToZone(booking.StartsAt, user.TimeZone);
        var label = newStatus == "completed" ? "✅ Выполнена" : "🕳 Клиент не пришёл";

        await bot.SendMessage(chatId,
            $"{label}: {startLocal:dd.MM в HH:mm}, {booking.Service.Name}",
            cancellationToken: ct);

        _logger.LogInformation(
            "Мастер отметил запись id={BookingId} как {Status}",
            bookingId, newStatus);

        // Возврат к списку
        await SendMasterBookingsAsync(bot, chatId, callback.From.Id, ct);
    }
}