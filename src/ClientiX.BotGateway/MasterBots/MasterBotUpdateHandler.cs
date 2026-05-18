using ClientiX.Infrastructure.Persistence;
using ClientiX.Infrastructure.Repositories;
using ClientiX.Infrastructure.State;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static StackExchange.Redis.Role;

namespace ClientiX.BotGateway.MasterBots;

/// <summary>
/// Общий обработчик апдейтов для всех ботов мастеров.
/// Получает контекст бота (кто его владелец) и нужный TelegramBotClient.
/// </summary>
public class MasterBotUpdateHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MasterBotUpdateHandler> _logger;
    private readonly UserStateService _states;

    public MasterBotUpdateHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<MasterBotUpdateHandler> logger,
    UserStateService states)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _states = states;
    }

    public async Task HandleAsync(
        MasterBotContext ctx, ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            if (update.Message is { Text: not null } message)
            {
                _logger.LogInformation(
                    "Бот @{BotUsername}: сообщение от @{From} (id={UserId}): {Text}",
                    ctx.BotUsername,
                    message.From?.Username ?? "—",
                    message.From?.Id,
                    message.Text);

                if (message.Text.StartsWith("/start"))
                {
                    await HandleClientStartAsync(ctx, bot, message, ct);
                }
                else
                {
                    await HandleClientTextAsync(ctx, bot, message, ct);
                }
            }
            else if (update.CallbackQuery is { } callback)
            {
                await HandleCallbackAsync(ctx, bot, callback, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Ошибка обработки апдейта в боте @{BotUsername}", ctx.BotUsername);
        }
    }

    public async Task HandleErrorAsync(
        MasterBotContext ctx, ITelegramBotClient bot, Exception exception,
        HandleErrorSource source, CancellationToken ct)
    {
        bool isTransient = IsTransient(exception);

        if (isTransient)
        {
            _logger.LogWarning(
                "Сетевой обрыв в боте @{BotUsername}: {Message}. Переподключаюсь.",
                ctx.BotUsername, exception.Message);
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
        else
        {
            _logger.LogError(exception,
                "Ошибка в боте @{BotUsername} (source: {Source})",
                ctx.BotUsername, source);
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }
    }

    /// <summary>
    /// Клиент (не мастер) написал /start боту мастера.
    /// Показываем карточку мастера и меню действий.
    /// </summary>
    private async Task HandleClientStartAsync(
        MasterBotContext ctx, ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        if (message.From is null) return;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var master = await users.GetByTelegramIdAsync(/* master telegramId */ 0, ct);

        // Точнее найдём мастера по нашему внутреннему UserId из контекста
        master = await users.GetByIdAsync(ctx.UserId, ct);
        if (master is null)
        {
            await bot.SendMessage(message.Chat.Id,
                "Мастер недоступен. Попробуйте позже.",
                cancellationToken: ct);
            return;
        }

        var nicheText = master.ManagedBot?.Niche switch
        {
            "nails" => "💅 Мастер маникюра",
            "barber" => "✂️ Парикмахер",
            "tattoo" => "🎨 Тату-мастер",
            "lashes" => "👁 Мастер ресниц и бровей",
            "beauty" => "💆 Косметолог",
            _ => "🌸 Бьюти-мастер"
        };

        var greeting =
            $"👋 Здравствуйте! Это бот мастера {master.FirstName ?? "—"}.\n\n" +
            $"{nicheText}\n" +
            (string.IsNullOrEmpty(master.ManagedBot?.City) ? "" : $"📍 {master.ManagedBot.City}\n") +
            $"\nЧто вы хотите сделать?";

        var keyboard = new InlineKeyboardMarkup(new[]
{
    new[] { InlineKeyboardButton.WithCallbackData("📋 Услуги и цены", "client_services") },
    new[] { InlineKeyboardButton.WithCallbackData("🖼 Портфолио", "client_portfolio") },
    new[] { InlineKeyboardButton.WithCallbackData("📅 Записаться", "client_book") },
    new[] { InlineKeyboardButton.WithCallbackData("📒 Мои записи", "client_my_bookings") },
    new[] { InlineKeyboardButton.WithCallbackData("ℹ️ О мастере", "client_about") },
});

        await bot.SendMessage(message.Chat.Id, greeting,
            replyMarkup: keyboard, cancellationToken: ct);
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

    private async Task HandleCallbackAsync(
    MasterBotContext ctx, ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var chatId = callback.Message!.Chat.Id;
        var data = callback.Data ?? "";

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var master = await users.GetByIdAsync(ctx.UserId, ct);
        if (master is null) return;

        // Бронирование: выбор услуги
        if (data.StartsWith("book_svc:"))
        {
            await HandleBookServiceChosenAsync(ctx, bot, callback, ct);
            return;
        }

        // Бронирование: выбор даты
        if (data.StartsWith("book_date:"))
        {
            await HandleBookDateChosenAsync(ctx, bot, callback, ct);
            return;
        }

        // Бронирование: выбор времени
        if (data.StartsWith("book_slot:"))
        {
            await HandleBookSlotChosenAsync(ctx, bot, callback, ct);
            return;
        }

        // Подтверждение бронирования
        if (data == "book_confirm")
        {
            await HandleBookConfirmAsync(ctx, bot, callback, ct);
            return;
        }

        if (data == "book_cancel")
        {
            await HandleBookCancelFsmAsync(ctx, bot, callback, ct);
            return;
        }

        if (data.StartsWith("client_cancel_booking:"))
        {
            await HandleClientCancelBookingAsync(ctx, bot, callback, ct);
            return;
        }

        if (data.StartsWith("cli_cal_nav:"))
        {
            await HandleClientCalendarNavAsync(ctx, bot, callback, ct);
            return;
        }

        if (data == "noop")
        {
            return; // пустая кнопка
        }

        if (data.StartsWith("client_reschedule:"))
        {
            await StartRescheduleFlowAsync(ctx, bot, callback, ct);
            return;
        }

        if (data.StartsWith("resched_date:"))
        {
            await HandleRescheduleDateAsync(ctx, bot, callback, ct);
            return;
        }

        if (data.StartsWith("resched_slot:"))
        {
            await HandleRescheduleSlotAsync(ctx, bot, callback, ct);
            return;
        }

        if (data == "resched_confirm")
        {
            await HandleRescheduleConfirmAsync(ctx, bot, callback, ct);
            return;
        }

        switch (data)
        {
            case "client_services":
                await SendServicesAsync(bot, chatId, master.Id, users, ct);
                break;

            case "client_portfolio":
                await SendPortfolioAsync(bot, chatId, ctx, users, ct);
                break;

            case "client_about":
                await SendAboutMasterAsync(bot, chatId, master, ct);
                break;

            case "client_book":
                await StartBookingFlowAsync(ctx, bot, chatId, callback.From.Id, users, ct);
                break;

            case "client_menu":
                await SendClientMenuAsync(bot, chatId, master, ct);
                break;

            case "client_my_bookings":
                await SendClientBookingsAsync(ctx, bot, chatId, callback.From.Id, users, ct);
                break;

            default:
                await bot.SendMessage(chatId, "Команда не распознана.",
                    cancellationToken: ct);
                break;
        }
    }

    private async Task SendServicesAsync(
        ITelegramBotClient bot, long chatId, long masterId,
        UserRepository users, CancellationToken ct)
    {
        var services = await users.GetServicesAsync(masterId, ct);

        string text;
        if (services.Count == 0)
        {
            text = "📋 Услуги\n\nМастер пока не добавил услуги в каталог.";
        }
        else
        {
            text = "📋 Услуги мастера:\n\n" +
                string.Join("\n\n", services.Select((s, i) =>
                    $"{i + 1}. <b>{System.Net.WebUtility.HtmlEncode(s.Name)}</b>\n" +
                    $"   ⏱ {s.DurationMinutes} мин\n" +
                    $"   💰 {s.PriceRub} руб."));
        }

        var keyboard = new InlineKeyboardMarkup(new[]
        {
        new[] { InlineKeyboardButton.WithCallbackData("📅 Записаться", "client_book") },
        new[] { InlineKeyboardButton.WithCallbackData("« В меню", "client_menu") }
    });

        await bot.SendMessage(chatId, text,
            parseMode: ParseMode.Html,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task SendPortfolioAsync(
    ITelegramBotClient bot, long chatId, MasterBotContext ctx,
    UserRepository users, CancellationToken ct)
    {
        var items = await users.GetPortfolioAsync(ctx.UserId, ct);

        if (items.Count == 0)
        {
            await bot.SendMessage(chatId,
                "🖼 Портфолио\n\nМастер пока не загрузил работы.",
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                new[] { InlineKeyboardButton.WithCallbackData("« В меню", "client_menu") }
                }),
                cancellationToken: ct);
            return;
        }

        await bot.SendMessage(chatId,
            $"🖼 Портфолио мастера ({items.Count} {GetWorksWord(items.Count)})",
            cancellationToken: ct);

        var botKey = ctx.BotTelegramId.ToString();

        foreach (var item in items)
        {
            // Проверяем, есть ли уже file_id для этого бота
            string? fileIdForThisBot = null;
            if (item.FileIdsPerBot is not null && item.FileIdsPerBot.TryGetValue(botKey, out var cached))
            {
                fileIdForThisBot = cached;
            }

            try
            {
                if (fileIdForThisBot is not null)
                {
                    // Быстрый путь: file_id уже знаком этому боту
                    await bot.SendPhoto(
                        chatId: chatId,
                        photo: InputFile.FromFileId(fileIdForThisBot),
                        caption: item.Caption,
                        cancellationToken: ct);
                }
                else
                {
                    // Первая отправка через этого бота: скачиваем из главного бота,
                    // загружаем в этот, кэшируем новый file_id.
                    await SendPhotoCrossBotAsync(bot, chatId, item, botKey, users, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Не удалось отправить фото портфолио id={ItemId} через бот @{Bot}",
                    item.Id, ctx.BotUsername);
            }
        }

        await bot.SendMessage(chatId,
            "Понравились работы? Запишитесь к мастеру 👇",
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
            new[] { InlineKeyboardButton.WithCallbackData("📅 Записаться", "client_book") },
            new[] { InlineKeyboardButton.WithCallbackData("« В меню", "client_menu") }
            }),
            cancellationToken: ct);
    }

    /// <summary>
    /// Скачивает фото из главного бота и загружает в бот мастера, кэширует новый file_id.
    /// </summary>
    private async Task SendPhotoCrossBotAsync(
        ITelegramBotClient masterBot, long chatId,
        Domain.Entities.PortfolioItem item, string botKey,
        UserRepository users, CancellationToken ct)
    {
        // Достаём главный бот из DI
        using var scope = _scopeFactory.CreateScope();
        var mainBot = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();

        // Скачиваем файл из главного бота
        using var stream = new MemoryStream();
        var file = await mainBot.GetFile(item.TelegramFileId, ct);
        if (file.FilePath is null) return;

        await mainBot.DownloadFile(file.FilePath, stream, ct);
        stream.Position = 0;

        // Загружаем в бот мастера, отправляем клиенту
        var sentMessage = await masterBot.SendPhoto(
            chatId: chatId,
            photo: InputFile.FromStream(stream, "photo.jpg"),
            caption: item.Caption,
            cancellationToken: ct);

        // Берём file_id, который получился у бота мастера
        var newFileId = sentMessage.Photo?.LastOrDefault()?.FileId;
        if (newFileId is not null)
        {
            await users.SetPortfolioFileIdForBotAsync(item.Id, long.Parse(botKey), newFileId, ct);
            _logger.LogInformation(
                "Сохранили file_id портфолио для бота {BotKey}: item={ItemId}",
                botKey, item.Id);
        }
    }

    private async Task SendAboutMasterAsync(
        ITelegramBotClient bot, long chatId, Domain.Entities.User master, CancellationToken ct)
    {
        var nicheText = master.ManagedBot?.Niche switch
        {
            "nails" => "💅 Маникюр и педикюр",
            "barber" => "✂️ Парикмахер / барбер",
            "tattoo" => "🎨 Тату-мастер",
            "lashes" => "👁 Ресницы и брови",
            "beauty" => "💆 Косметология и массаж",
            _ => "🌸 Бьюти-мастер"
        };

        var text =
            $"ℹ️ О мастере\n\n" +
            $"👤 {master.FirstName ?? "—"} {master.LastName ?? ""}\n" +
            $"💼 {nicheText}\n" +
            (string.IsNullOrEmpty(master.ManagedBot?.City)
                ? ""
                : $"📍 {master.ManagedBot.City}\n");

        var keyboard = new InlineKeyboardMarkup(new[]
        {
        new[] { InlineKeyboardButton.WithCallbackData("📅 Записаться", "client_book") },
        new[] { InlineKeyboardButton.WithCallbackData("« В меню", "client_menu") }
    });

        await bot.SendMessage(chatId, text,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task SendClientMenuAsync(
        ITelegramBotClient bot, long chatId,
        Domain.Entities.User master, CancellationToken ct)
    {
        var nicheText = master.ManagedBot?.Niche switch
        {
            "nails" => "💅 Мастер маникюра",
            "barber" => "✂️ Парикмахер",
            "tattoo" => "🎨 Тату-мастер",
            "lashes" => "👁 Мастер ресниц и бровей",
            "beauty" => "💆 Косметолог",
            _ => "🌸 Бьюти-мастер"
        };

        var text =
            $"Главное меню бота {master.FirstName ?? "мастера"}\n\n" +
            $"{nicheText}\n" +
            (string.IsNullOrEmpty(master.ManagedBot?.City)
                ? ""
                : $"📍 {master.ManagedBot.City}\n") +
            $"\nЧто вы хотите сделать?";

        var keyboard = new InlineKeyboardMarkup(new[]
{
    new[] { InlineKeyboardButton.WithCallbackData("📋 Услуги и цены", "client_services") },
    new[] { InlineKeyboardButton.WithCallbackData("🖼 Портфолио", "client_portfolio") },
    new[] { InlineKeyboardButton.WithCallbackData("📅 Записаться", "client_book") },
    new[] { InlineKeyboardButton.WithCallbackData("📒 Мои записи", "client_my_bookings") },
    new[] { InlineKeyboardButton.WithCallbackData("ℹ️ О мастере", "client_about") },
});

        await bot.SendMessage(chatId, text, replyMarkup: keyboard, cancellationToken: ct);
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

    // ============================================================
    // БРОНИРОВАНИЕ
    // ============================================================

    private async Task StartBookingFlowAsync(
        MasterBotContext ctx, ITelegramBotClient bot, long chatId, long clientTelegramId,
        UserRepository users, CancellationToken ct)
    {
        var services = await users.GetServicesAsync(ctx.UserId, ct);
        if (services.Count == 0)
        {
            await bot.SendMessage(chatId,
                "🚫 У мастера пока нет услуг в каталоге.",
                cancellationToken: ct);
            return;
        }

        // Сохраняем состояние бронирования в Redis под ключ "fsm:user:{clientTelegramId}"
        var state = new UserState
        {
            CurrentStep = "booking_service",
            Data = { ["master_user_id"] = ctx.UserId.ToString() }
        };
        await _states.SetAsync(clientTelegramId, state);

        var text = "📅 Запись на услугу\n\n" +
                   "Шаг 1 из 3: выберите услугу.";

        var buttons = services.Select(s => new[]
        {
        InlineKeyboardButton.WithCallbackData(
            $"{s.Name} — {s.DurationMinutes} мин, {s.PriceRub}₽",
            $"book_svc:{s.Id}")
    }).ToList();
        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("« Отмена", "client_menu") });

        await bot.SendMessage(chatId, text,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task HandleBookServiceChosenAsync(
    MasterBotContext ctx, ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        if (!long.TryParse(callback.Data!.Replace("book_svc:", ""), out var serviceId)) return;

        var chatId = callback.Message!.Chat.Id;
        var clientTgId = callback.From.Id;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var slots = scope.ServiceProvider
            .GetRequiredService<ClientiX.Infrastructure.Bookings.BookingSlotService>();

        var master = await users.GetByIdAsync(ctx.UserId, ct);
        var masterTz = master?.TimeZone ?? "Europe/Moscow";

        var services = await users.GetServicesAsync(ctx.UserId, ct);
        
        var service = services.FirstOrDefault(s => s.Id == serviceId);
        if (service is null)
        {
            await bot.SendMessage(chatId, "Услуга не найдена.", cancellationToken: ct);
            return;
        }

        var state = await _states.GetAsync(clientTgId)
            ?? new UserState { Data = { ["master_user_id"] = ctx.UserId.ToString() } };
        state.CurrentStep = "booking_date";
        state.Data["service_id"] = service.Id.ToString();
        state.Data["service_duration"] = service.DurationMinutes.ToString();
        state.Data["service_price"] = service.PriceRub.ToString();
        state.Data["service_name"] = service.Name;
        state.Data["master_tz"] = masterTz;
        await _states.SetAsync(clientTgId, state);

        var days = await slots.GetDaysWithAvailabilityAsync(
            ctx.UserId, service.DurationMinutes,
            master?.BookingHorizonDays ?? 14,
            DateTime.UtcNow, masterTz, ct);

        var freeDays = days.Where(d => d.HasFreeSlot).ToList();
        if (freeDays.Count == 0)
        {
            await bot.SendMessage(chatId,
                "😔 К сожалению, у мастера нет свободных слотов под эту услугу в ближайшее время.\n\n" +
                "Попробуйте позже или свяжитесь с мастером напрямую.",
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
            new[] { InlineKeyboardButton.WithCallbackData("« В меню", "client_menu") }
                }),
                cancellationToken: ct);
            await _states.ClearAsync(clientTgId);
            return;
        }

        // Рендерим календарь текущего месяца с раскраской свободных дней
        var todayLocal = ClientiX.Infrastructure.TimeZones.NowInZone(masterTz).Date;
        var firstOfMonth = new DateTime(todayLocal.Year, todayLocal.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        await SendClientCalendarAsync(bot, chatId, clientTgId, ctx.UserId, service, masterTz, firstOfMonth, days, ct);
    }

    private async Task HandleBookDateChosenAsync(
        MasterBotContext ctx, ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var dateStr = callback.Data!.Replace("book_date:", "");
        if (!DateTime.TryParseExact(dateStr, "yyyy-MM-dd", null,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var date))
            return;

        date = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

        var chatId = callback.Message!.Chat.Id;
        var clientTgId = callback.From.Id;

        var state = await _states.GetAsync(clientTgId);
        if (state is null) return;

        if (!int.TryParse(state.Data.GetValueOrDefault("service_duration", "0"), out var duration)
            || duration == 0) return;

        var masterTz = state.Data.GetValueOrDefault("master_tz", "Europe/Moscow");

        using var scope = _scopeFactory.CreateScope();
        var slotsService = scope.ServiceProvider
            .GetRequiredService<ClientiX.Infrastructure.Bookings.BookingSlotService>();

        var availableSlots = await slotsService.GetAvailableSlotsAsync(
            ctx.UserId, duration, date, DateTime.UtcNow, masterTz, ct);

        if (availableSlots.Count == 0)
        {
            await bot.SendMessage(chatId,
                "На эту дату слотов больше нет (видимо, занялись пока вы выбирали).\n" +
                "Попробуйте другую дату.",
                cancellationToken: ct);
            return;
        }

        state.CurrentStep = "booking_slot";
        state.Data["master_tz"] = masterTz;
        await _states.SetAsync(clientTgId, state);

        var serviceName = state.Data.GetValueOrDefault("service_name", "услугу");
        var text = $"📅 {date:dddd, dd MMMM}\n" +
                   $"📋 {serviceName}\n\n" +
                   $"Шаг 3 из 3: выберите время.";

        // Рендерим кнопки по 3 в ряд
        var buttons = new List<InlineKeyboardButton[]>();
        var row = new List<InlineKeyboardButton>();
        foreach (var slot in availableSlots)
        {
            var slotLocal = ClientiX.Infrastructure.TimeZones.ToZone(slot, masterTz);
            row.Add(InlineKeyboardButton.WithCallbackData(
                slotLocal.ToString("HH:mm"),
                $"book_slot:{slot:yyyy-MM-ddTHH:mm}"));

            if (row.Count == 3)
            {
                buttons.Add(row.ToArray());
                row = new List<InlineKeyboardButton>();
            }
        }
        if (row.Count > 0) buttons.Add(row.ToArray());

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("« Отмена", "book_cancel") });

        await bot.SendMessage(chatId, text,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task HandleBookSlotChosenAsync(
        MasterBotContext ctx, ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var slotStr = callback.Data!.Replace("book_slot:", "");
        if (!DateTime.TryParseExact(slotStr, "yyyy-MM-ddTHH:mm", null,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var slot))
            return;
        slot = DateTime.SpecifyKind(slot, DateTimeKind.Utc);

        var chatId = callback.Message!.Chat.Id;
        var clientTgId = callback.From.Id;

        var state = await _states.GetAsync(clientTgId);
        if (state is null) return;

        state.CurrentStep = "booking_confirm";
        state.Data["slot"] = slot.ToString("O");
        await _states.SetAsync(clientTgId, state);

        var serviceName = state.Data.GetValueOrDefault("service_name", "услугу");
        var duration = int.Parse(state.Data.GetValueOrDefault("service_duration", "0"));
        var price = int.Parse(state.Data.GetValueOrDefault("service_price", "0"));

        var endTime = slot.AddMinutes(duration);

        var text = "📋 Проверьте запись:\n\n" +
                   $"📅 Дата: {slot:dddd, dd MMMM yyyy}\n" +
                   $"🕐 Время: {slot:HH:mm} – {endTime:HH:mm}\n" +
                   $"💼 Услуга: {serviceName}\n" +
                   $"⏱ Длительность: {duration} мин\n" +
                   $"💰 Стоимость: {price} руб.\n\n" +
                   "Подтверждаете запись?";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
        new[] { InlineKeyboardButton.WithCallbackData("✅ Подтвердить", "book_confirm") },
        new[] { InlineKeyboardButton.WithCallbackData("« Отмена", "book_cancel") }
    });

        await bot.SendMessage(chatId, text,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task HandleBookConfirmAsync(
        MasterBotContext ctx, ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var chatId = callback.Message!.Chat.Id;
        var clientTgId = callback.From.Id;

        var state = await _states.GetAsync(clientTgId);
        if (state is null || state.CurrentStep != "booking_confirm")
        {
            await bot.SendMessage(chatId,
                "Сессия записи устарела. Нажмите /start, чтобы начать заново.",
                cancellationToken: ct);
            return;
        }

        if (!long.TryParse(state.Data.GetValueOrDefault("service_id", "0"), out var serviceId)) return;
        if (!int.TryParse(state.Data.GetValueOrDefault("service_duration", "0"), out var duration)) return;
        if (!int.TryParse(state.Data.GetValueOrDefault("service_price", "0"), out var price)) return;
        if (!DateTime.TryParse(state.Data.GetValueOrDefault("slot", ""), null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var slot)) return;

        slot = DateTime.SpecifyKind(slot, DateTimeKind.Utc);
        var endsAt = slot.AddMinutes(duration);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClientiXDbContext>();

        // Создаём запись. Идемпотентно: уникальный частичный индекс защищает от двойного бронирования.
        var booking = new Domain.Entities.Booking
        {
            UserId = ctx.UserId,
            ServiceId = serviceId,
            ClientTelegramId = clientTgId,
            ClientFirstName = callback.From.FirstName,
            ClientUsername = callback.From.Username,
            StartsAt = slot,
            EndsAt = endsAt,
            DurationMinutes = duration,
            PriceRub = price,
            Status = "confirmed", // авто-подтверждение
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            db.Bookings.Add(booking);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("idx_bookings_no_overlap") == true)
        {
            await bot.SendMessage(chatId,
                "🚫 Кто-то опередил вас и забронировал это время. Попробуйте другой слот.",
                cancellationToken: ct);
            return;
        }

        await _states.ClearAsync(clientTgId);

        var serviceName = state.Data.GetValueOrDefault("service_name", "услугу");

        // Сообщение клиенту
        await bot.SendMessage(chatId,
            "✅ Запись подтверждена!\n\n" +
            $"📅 {slot:dddd, dd MMMM} в {slot:HH:mm}\n" +
            $"💼 {serviceName}\n\n" +
            "Ждём вас! Если планы изменятся — отмените запись здесь же.",
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
            new[] { InlineKeyboardButton.WithCallbackData("« В меню", "client_menu") }
            }),
            cancellationToken: ct);

        _logger.LogInformation(
            "Создана запись id={BookingId}, master={MasterId}, client={ClientTgId}, when={Slot}",
            booking.Id, ctx.UserId, clientTgId, slot);

        // Уведомление мастеру через @cl1ent1x_bot
        await NotifyMasterAboutNewBookingAsync(scope, ctx.UserId, booking, ct);
    }

    private async Task HandleBookCancelFsmAsync(
        MasterBotContext ctx, ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var chatId = callback.Message!.Chat.Id;
        await _states.ClearAsync(callback.From.Id);

        await bot.SendMessage(chatId,
            "❌ Запись отменена.",
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
            new[] { InlineKeyboardButton.WithCallbackData("« В меню", "client_menu") }
            }),
            cancellationToken: ct);
    }

    private async Task NotifyMasterAboutNewBookingAsync(
        IServiceScope scope, long masterUserId, Domain.Entities.Booking booking, CancellationToken ct)
    {
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var master = await users.GetByIdAsync(masterUserId, ct);
        if (master is null) return;

        var services = await users.GetServicesAsync(masterUserId, ct);
        var service = services.FirstOrDefault(s => s.Id == booking.ServiceId);
        var serviceName = service?.Name ?? "услугу";

        var clientName = !string.IsNullOrEmpty(booking.ClientFirstName)
            ? booking.ClientFirstName
            : "клиент";
        var clientLink = !string.IsNullOrEmpty(booking.ClientUsername)
            ? $"@{booking.ClientUsername}"
            : $"<a href=\"tg://user?id={booking.ClientTelegramId}\">профиль</a>";

        var text = "🔔 Новая запись!\n\n" +
                   $"👤 Клиент: {clientName} ({clientLink})\n" +
                   $"📅 Дата: {booking.StartsAt:dddd, dd MMMM yyyy}\n" +
                   $"🕐 Время: {booking.StartsAt:HH:mm} – {booking.EndsAt:HH:mm}\n" +
                   $"💼 Услуга: {serviceName}\n" +
                   $"💰 Стоимость: {booking.PriceRub} руб.";

        try
        {
            var mainBot = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
            await mainBot.SendMessage(
                chatId: master.TelegramId,
                text: text,
                parseMode: ParseMode.Html,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Не удалось уведомить мастера {MasterId} о новой записи {BookingId}",
                masterUserId, booking.Id);
        }
    }

    private async Task HandleClientTextAsync(
        MasterBotContext ctx, ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        // У клиентов бота мастера пока нет FSM-шагов с вводом текста.
        // На всякий случай — если кто-то напишет произвольный текст — просто покажем меню.
        await bot.SendMessage(
            chatId: message.Chat.Id,
            text: "Нажмите /start, чтобы открыть меню записи.",
            cancellationToken: ct);
    }

    private static string GetRussianDayShort(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "пн",
        DayOfWeek.Tuesday => "вт",
        DayOfWeek.Wednesday => "ср",
        DayOfWeek.Thursday => "чт",
        DayOfWeek.Friday => "пт",
        DayOfWeek.Saturday => "сб",
        DayOfWeek.Sunday => "вс",
        _ => "?"
    };

    private async Task SendClientBookingsAsync(
    MasterBotContext ctx, ITelegramBotClient bot, long chatId, long clientTelegramId,
    UserRepository users, CancellationToken ct)
    {
        var bookings = await users.GetClientBookingsAsync(clientTelegramId, ctx.UserId, ct);

        var master = await users.GetByIdAsync(ctx.UserId, ct);
        var masterTz = master?.TimeZone ?? "Europe/Moscow";

        if (bookings.Count == 0)
        {
            await bot.SendMessage(chatId,
                "📒 Мои записи\n\nУ вас пока нет активных записей.",
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                new[] { InlineKeyboardButton.WithCallbackData("📅 Записаться", "client_book") },
                new[] { InlineKeyboardButton.WithCallbackData("« В меню", "client_menu") }
                }),
                cancellationToken: ct);
            return;
        }

        var text = "📒 Ваши активные записи:\n\n" +
            string.Join("\n\n", bookings.Select(b =>
                $"📅 {b.StartsAt:dddd, dd MMMM} в {b.StartsAt:HH:mm}\n" +
                $"💼 {b.Service.Name} ({b.DurationMinutes} мин, {b.PriceRub} руб.)\n" +
                $"📌 Статус: {GetBookingStatusEmoji(b.Status)} {GetBookingStatusName(b.Status)}"));

        text += "\n\nНажмите на запись ниже, чтобы отменить.";

        var buttons = new List<InlineKeyboardButton[]>();
        foreach (var b in bookings.Take(10))
        {
            var startLocal = ClientiX.Infrastructure.TimeZones.ToZone(b.StartsAt, masterTz);
            buttons.Add(new[]
            {
        InlineKeyboardButton.WithCallbackData(
            $"🔄 Перенести {startLocal:dd.MM HH:mm}",
            $"client_reschedule:{b.Id}"),
    });
            buttons.Add(new[]
            {
        InlineKeyboardButton.WithCallbackData(
            $"❌ Отменить {startLocal:dd.MM HH:mm}",
            $"client_cancel_booking:{b.Id}")
    });
        }
        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("« В меню", "client_menu") });

        await bot.SendMessage(chatId, text,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task HandleClientCancelBookingAsync(
        MasterBotContext ctx, ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        if (!long.TryParse(callback.Data!.Replace("client_cancel_booking:", ""), out var bookingId))
            return;

        var chatId = callback.Message!.Chat.Id;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();

        var booking = await users.GetBookingByIdAsync(bookingId, ct);
        if (booking is null || booking.ClientTelegramId != callback.From.Id || booking.UserId != ctx.UserId)
        {
            await bot.SendMessage(chatId, "Запись не найдена.", cancellationToken: ct);
            return;
        }

        var ok = await users.CancelBookingAsync(bookingId, "client", null, ct);
        if (!ok)
        {
            await bot.SendMessage(chatId, "Не удалось отменить.", cancellationToken: ct);
            return;
        }

        await bot.SendMessage(chatId,
            $"✅ Запись отменена:\n📅 {booking.StartsAt:dd.MM в HH:mm}\n💼 {booking.Service.Name}",
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
            new[] { InlineKeyboardButton.WithCallbackData("« В меню", "client_menu") }
            }),
            cancellationToken: ct);

        _logger.LogInformation(
            "Клиент отменил запись id={BookingId}, master={MasterId}, client={ClientTgId}",
            booking.Id, ctx.UserId, callback.From.Id);

        // Уведомляем мастера
        await NotifyMasterAboutCancellationAsync(scope, ctx.UserId, booking, "client", ct);
    }

    private async Task NotifyMasterAboutCancellationAsync(
        IServiceScope scope, long masterUserId,
        Domain.Entities.Booking booking, string cancelledBy, CancellationToken ct)
    {
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var master = await users.GetByIdAsync(masterUserId, ct);
        if (master is null) return;

        var clientName = !string.IsNullOrEmpty(booking.ClientFirstName)
            ? booking.ClientFirstName
            : "Клиент";
        var clientLink = !string.IsNullOrEmpty(booking.ClientUsername)
            ? $"@{booking.ClientUsername}"
            : $"<a href=\"tg://user?id={booking.ClientTelegramId}\">профиль</a>";

        var text = "❌ Запись отменена клиентом\n\n" +
                   $"👤 Клиент: {clientName} ({clientLink})\n" +
                   $"📅 {booking.StartsAt:dddd, dd MMMM} в {booking.StartsAt:HH:mm}\n" +
                   $"💼 {booking.Service.Name}";

        try
        {
            var mainBot = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
            await mainBot.SendMessage(master.TelegramId, text,
                parseMode: ParseMode.Html, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось уведомить мастера об отмене");
        }
    }

    private static string GetBookingStatusEmoji(string status) => status switch
    {
        "pending" => "⏳",
        "confirmed" => "✅",
        "completed" => "✔️",
        "cancelled_by_client" => "❌",
        "cancelled_by_master" => "🚫",
        _ => "•"
    };

    private static string GetBookingStatusName(string status) => status switch
    {
        "pending" => "ожидание",
        "confirmed" => "подтверждена",
        "completed" => "завершена",
        "cancelled_by_client" => "отменена клиентом",
        "cancelled_by_master" => "отменена мастером",
        _ => status
    };

    // ============================================================
    // КАЛЕНДАРЬ ДЛЯ КЛИЕНТА
    // ============================================================

    private async Task SendClientCalendarAsync(
        ITelegramBotClient bot, long chatId, long clientTgId,
        long masterUserId, Domain.Entities.Service service,
        string masterTz, DateTime firstOfMonth,
        List<(DateTime Date, bool HasFreeSlot)> availabilityDays,
        CancellationToken ct)
    {
        var todayLocal = ClientiX.Infrastructure.TimeZones.NowInZone(masterTz).Date;
        int daysInMonth = DateTime.DaysInMonth(firstOfMonth.Year, firstOfMonth.Month);
        int firstWeekday = ((int)firstOfMonth.DayOfWeek + 6) % 7; // понедельник = 0

        // Превращаем список дат с доступностью в словарь для быстрого поиска
        var freeMap = availabilityDays.ToDictionary(d => d.Date.Date, d => d.HasFreeSlot);

        var ruMonths = new[] { "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь",
                       "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь" };
        var monthName = $"{ruMonths[firstOfMonth.Month - 1]} {firstOfMonth.Year}";

        var text = $"📅 Выберите дату\n\n" +
                   $"💼 <b>{System.Net.WebUtility.HtmlEncode(service.Name)}</b>\n" +
                   $"⏱ {service.DurationMinutes} мин · 💰 {service.PriceRub} ₽\n\n" +
                   $"<b>{monthName}</b>\n\n" +
                   $"🟢 свободно   ⚪ нет окон";

        var buttons = new List<InlineKeyboardButton[]>();

        // Навигация по месяцам (только в пределах горизонта)
        var prevMonth = firstOfMonth.AddMonths(-1);
        var nextMonth = firstOfMonth.AddMonths(1);

        var navPrev = prevMonth.Year > todayLocal.Year ||
                      (prevMonth.Year == todayLocal.Year && prevMonth.Month >= todayLocal.Month)
            ? InlineKeyboardButton.WithCallbackData("«", $"cli_cal_nav:{prevMonth:yyyy-MM}")
            : InlineKeyboardButton.WithCallbackData(" ", "noop");

        // Стрелка вперёд показывается, только если в следующем месяце есть хоть один свободный слот
        bool nextHasSlots = availabilityDays.Any(d =>
            d.Date.Year == nextMonth.Year && d.Date.Month == nextMonth.Month && d.HasFreeSlot);
        var navNext = nextHasSlots
            ? InlineKeyboardButton.WithCallbackData("»", $"cli_cal_nav:{nextMonth:yyyy-MM}")
            : InlineKeyboardButton.WithCallbackData(" ", "noop");

        buttons.Add(new[]
        {
        navPrev,
        InlineKeyboardButton.WithCallbackData(
            monthName, "noop"),
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
            bool hasFree = freeMap.TryGetValue(date.Date, out var f) && f;
            bool isPast = date.Date < todayLocal;

            if (isPast || !hasFree)
            {
                row.Add(InlineKeyboardButton.WithCallbackData($"⚪{day}", "noop"));
            }
            else
            {
                row.Add(InlineKeyboardButton.WithCallbackData(
                    $"🟢{day}",
                    $"book_date:{date:yyyy-MM-dd}"));
            }

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

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("« Отмена", "book_cancel") });

        await bot.SendMessage(chatId, text,
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task HandleClientCalendarNavAsync(
        MasterBotContext ctx, ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var ymStr = callback.Data!.Replace("cli_cal_nav:", "");
        if (!DateTime.TryParseExact(ymStr + "-01", "yyyy-MM-dd", null,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var newMonth))
            return;

        var chatId = callback.Message!.Chat.Id;
        var clientTgId = callback.From.Id;

        var state = await _states.GetAsync(clientTgId);
        if (state is null || state.CurrentStep != "booking_date") return;

        if (!int.TryParse(state.Data.GetValueOrDefault("service_id", "0"), out var serviceId)) return;
        if (!int.TryParse(state.Data.GetValueOrDefault("service_duration", "0"), out var duration)) return;
        var masterTz = state.Data.GetValueOrDefault("master_tz", "Europe/Moscow");

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var slotsService = scope.ServiceProvider
            .GetRequiredService<ClientiX.Infrastructure.Bookings.BookingSlotService>();

        var services = await users.GetServicesAsync(ctx.UserId, ct);
        var service = services.FirstOrDefault(s => s.Id == serviceId);
        if (service is null) return;

        var master = await users.GetByIdAsync(ctx.UserId, ct);
        var horizon = master?.BookingHorizonDays ?? 14;

        var days = await slotsService.GetDaysWithAvailabilityAsync(
            ctx.UserId, duration, horizon, DateTime.UtcNow, masterTz, ct);

        var firstOfMonth = new DateTime(newMonth.Year, newMonth.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);

        await SendClientCalendarAsync(bot, chatId, clientTgId, ctx.UserId, service, masterTz, firstOfMonth, days, ct);
    }

    // ============================================================
    // ПЕРЕНОС ЗАПИСИ
    // ============================================================

    private async Task StartRescheduleFlowAsync(
        MasterBotContext ctx, ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        if (!long.TryParse(callback.Data!.Replace("client_reschedule:", ""), out var bookingId))
            return;

        var chatId = callback.Message!.Chat.Id;
        var clientTgId = callback.From.Id;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var slotsService = scope.ServiceProvider
            .GetRequiredService<ClientiX.Infrastructure.Bookings.BookingSlotService>();

        var booking = await users.GetBookingByIdAsync(bookingId, ct);
        if (booking is null || booking.ClientTelegramId != clientTgId || booking.UserId != ctx.UserId)
        {
            await bot.SendMessage(chatId, "Запись не найдена.", cancellationToken: ct);
            return;
        }

        var master = await users.GetByIdAsync(ctx.UserId, ct);
        var masterTz = master?.TimeZone ?? "Europe/Moscow";
        var horizon = master?.BookingHorizonDays ?? 14;

        // Получаем дни доступности под исходную услугу
        var days = await slotsService.GetDaysWithAvailabilityAsync(
            ctx.UserId, booking.DurationMinutes, horizon, DateTime.UtcNow, masterTz, ct);

        if (!days.Any(d => d.HasFreeSlot))
        {
            await bot.SendMessage(chatId,
                "😔 Нет свободных слотов для переноса в ближайшее время.",
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                new[] { InlineKeyboardButton.WithCallbackData("« К записям", "client_my_bookings") }
                }),
                cancellationToken: ct);
            return;
        }

        // Сохраняем FSM: переносим конкретную запись
        var state = new UserState
        {
            CurrentStep = "rescheduling_date",
            Data =
        {
            ["booking_id"] = bookingId.ToString(),
            ["service_duration"] = booking.DurationMinutes.ToString(),
            ["master_tz"] = masterTz,
            ["service_name"] = booking.Service.Name
        }
        };
        await _states.SetAsync(clientTgId, state);

        // Рендерим календарь (используем тот же метод, но с другим префиксом callback)
        var todayLocal = ClientiX.Infrastructure.TimeZones.NowInZone(masterTz).Date;
        var firstOfMonth = new DateTime(todayLocal.Year, todayLocal.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);

        await SendRescheduleCalendarAsync(bot, chatId, masterTz, booking, days, firstOfMonth, ct);
    }

    private async Task SendRescheduleCalendarAsync(
        ITelegramBotClient bot, long chatId, string masterTz,
        Domain.Entities.Booking booking,
        List<(DateTime Date, bool HasFreeSlot)> availabilityDays,
        DateTime firstOfMonth, CancellationToken ct)
    {
        var todayLocal = ClientiX.Infrastructure.TimeZones.NowInZone(masterTz).Date;
        var oldLocal = ClientiX.Infrastructure.TimeZones.ToZone(booking.StartsAt, masterTz);

        int daysInMonth = DateTime.DaysInMonth(firstOfMonth.Year, firstOfMonth.Month);
        int firstWeekday = ((int)firstOfMonth.DayOfWeek + 6) % 7;

        var freeMap = availabilityDays.ToDictionary(d => d.Date.Date, d => d.HasFreeSlot);

        var ruMonths = new[] { "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь",
                           "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь" };
        var monthName = $"{ruMonths[firstOfMonth.Month - 1]} {firstOfMonth.Year}";

        var text = $"🔄 Перенос записи\n\n" +
                   $"Текущая: <b>{oldLocal:dd.MM в HH:mm}</b>, {booking.Service.Name}\n\n" +
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
                row.Add(InlineKeyboardButton.WithCallbackData($"🟢{day}", $"resched_date:{date:yyyy-MM-dd}"));

            if (row.Count == 7) { buttons.Add(row.ToArray()); row = new List<InlineKeyboardButton>(); }
        }
        if (row.Count > 0)
        {
            while (row.Count < 7) row.Add(InlineKeyboardButton.WithCallbackData(" ", "noop"));
            buttons.Add(row.ToArray());
        }

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("« Отмена", "client_my_bookings") });

        await bot.SendMessage(chatId, text,
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task HandleRescheduleDateAsync(
        MasterBotContext ctx, ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var dateStr = callback.Data!.Replace("resched_date:", "");
        if (!DateTime.TryParseExact(dateStr, "yyyy-MM-dd", null,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var date))
            return;
        date = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);

        var chatId = callback.Message!.Chat.Id;
        var clientTgId = callback.From.Id;

        var state = await _states.GetAsync(clientTgId);
        if (state is null || state.CurrentStep != "rescheduling_date") return;

        var masterTz = state.Data.GetValueOrDefault("master_tz", "Europe/Moscow");
        if (!int.TryParse(state.Data.GetValueOrDefault("service_duration", "0"), out var duration)) return;

        using var scope = _scopeFactory.CreateScope();
        var slotsService = scope.ServiceProvider
            .GetRequiredService<ClientiX.Infrastructure.Bookings.BookingSlotService>();

        var slots = await slotsService.GetAvailableSlotsAsync(
            ctx.UserId, duration, date, DateTime.UtcNow, masterTz, ct);

        if (slots.Count == 0)
        {
            await bot.SendMessage(chatId,
                "На эту дату слотов больше нет. Попробуйте другую.",
                cancellationToken: ct);
            return;
        }

        state.CurrentStep = "rescheduling_slot";
        state.Data["new_date"] = date.ToString("O");
        await _states.SetAsync(clientTgId, state);

        var serviceName = state.Data.GetValueOrDefault("service_name", "услугу");
        var text = $"🔄 Перенос: {serviceName}\n📅 {date:dddd, dd MMMM}\n\nВыберите новое время:";

        var buttons = new List<InlineKeyboardButton[]>();
        var row = new List<InlineKeyboardButton>();
        foreach (var slot in slots)
        {
            var slotLocal = ClientiX.Infrastructure.TimeZones.ToZone(slot, masterTz);
            row.Add(InlineKeyboardButton.WithCallbackData(
                slotLocal.ToString("HH:mm"),
                $"resched_slot:{slot:yyyy-MM-ddTHH:mm}"));
            if (row.Count == 3) { buttons.Add(row.ToArray()); row = new List<InlineKeyboardButton>(); }
        }
        if (row.Count > 0) buttons.Add(row.ToArray());
        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("« Отмена", "client_my_bookings") });

        await bot.SendMessage(chatId, text,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    private async Task HandleRescheduleSlotAsync(
        MasterBotContext ctx, ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var slotStr = callback.Data!.Replace("resched_slot:", "");
        if (!DateTime.TryParseExact(slotStr, "yyyy-MM-ddTHH:mm", null,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var slot)) return;
        slot = DateTime.SpecifyKind(slot, DateTimeKind.Utc);

        var chatId = callback.Message!.Chat.Id;
        var clientTgId = callback.From.Id;

        var state = await _states.GetAsync(clientTgId);
        if (state is null) return;

        state.CurrentStep = "rescheduling_confirm";
        state.Data["new_slot"] = slot.ToString("O");
        await _states.SetAsync(clientTgId, state);

        var masterTz = state.Data.GetValueOrDefault("master_tz", "Europe/Moscow");
        var slotLocal = ClientiX.Infrastructure.TimeZones.ToZone(slot, masterTz);

        if (!long.TryParse(state.Data.GetValueOrDefault("booking_id", "0"), out var bookingId)) return;

        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var booking = await users.GetBookingByIdAsync(bookingId, ct);
        if (booking is null) return;

        var oldLocal = ClientiX.Infrastructure.TimeZones.ToZone(booking.StartsAt, masterTz);

        var text = "🔄 Подтвердите перенос:\n\n" +
                   $"💼 {booking.Service.Name}\n" +
                   $"📅 Было: {oldLocal:dd.MM в HH:mm}\n" +
                   $"📅 Станет: <b>{slotLocal:dd.MM в HH:mm}</b>\n\n" +
                   "Подтвердить?";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
        new[] { InlineKeyboardButton.WithCallbackData("✅ Перенести", "resched_confirm") },
        new[] { InlineKeyboardButton.WithCallbackData("« Отмена", "client_my_bookings") }
    });

        await bot.SendMessage(chatId, text,
            parseMode: ParseMode.Html,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task HandleRescheduleConfirmAsync(
        MasterBotContext ctx, ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var chatId = callback.Message!.Chat.Id;
        var clientTgId = callback.From.Id;

        var state = await _states.GetAsync(clientTgId);
        if (state is null || state.CurrentStep != "rescheduling_confirm")
        {
            await bot.SendMessage(chatId, "Сессия устарела. Начните перенос заново.",
                cancellationToken: ct);
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

        var booking = await users.GetBookingByIdAsync(bookingId, ct);
        if (booking is null) return;
        var oldStart = booking.StartsAt;

        var ok = await users.RescheduleBookingAsync(bookingId, newSlot, newEnd, ct);
        if (!ok)
        {
            await bot.SendMessage(chatId,
                "🚫 Это время уже занято другой записью. Выберите другое.",
                cancellationToken: ct);
            return;
        }

        await _states.ClearAsync(clientTgId);

        var masterTz = state.Data.GetValueOrDefault("master_tz", "Europe/Moscow");
        var newLocal = ClientiX.Infrastructure.TimeZones.ToZone(newSlot, masterTz);

        await bot.SendMessage(chatId,
            $"✅ Запись перенесена!\n\n📅 Новое время: {newLocal:dddd, dd MMMM в HH:mm}",
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
            new[] { InlineKeyboardButton.WithCallbackData("« В меню", "client_menu") }
            }),
            cancellationToken: ct);

        _logger.LogInformation(
            "Клиент перенёс запись id={BookingId}: {Old} → {New}",
            bookingId, oldStart, newSlot);

        // Уведомление мастеру через @cl1ent1x_bot
        await NotifyMasterAboutRescheduleAsync(scope, ctx.UserId, booking, oldStart, newSlot, ct);
    }

    private async Task NotifyMasterAboutRescheduleAsync(
        IServiceScope scope, long masterUserId,
        Domain.Entities.Booking booking, DateTime oldStart, DateTime newStart, CancellationToken ct)
    {
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var master = await users.GetByIdAsync(masterUserId, ct);
        if (master is null) return;

        var tz = master.TimeZone;
        var oldLocal = ClientiX.Infrastructure.TimeZones.ToZone(oldStart, tz);
        var newLocal = ClientiX.Infrastructure.TimeZones.ToZone(newStart, tz);

        var clientName = !string.IsNullOrEmpty(booking.ClientFirstName) ? booking.ClientFirstName : "Клиент";
        var clientLink = !string.IsNullOrEmpty(booking.ClientUsername)
            ? $"@{booking.ClientUsername}"
            : $"<a href=\"tg://user?id={booking.ClientTelegramId}\">профиль</a>";

        var text = "🔄 Клиент перенёс запись\n\n" +
                   $"👤 {clientName} ({clientLink})\n" +
                   $"💼 {booking.Service.Name}\n" +
                   $"📅 Было: {oldLocal:dd.MM в HH:mm}\n" +
                   $"📅 Стало: <b>{newLocal:dd.MM в HH:mm}</b>";

        try
        {
            var mainBot = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
            await mainBot.SendMessage(master.TelegramId, text,
                parseMode: ParseMode.Html, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось уведомить мастера о переносе");
        }
    }
}