using ClientiX.Infrastructure.Repositories;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ClientiX.BotGateway.MasterBots;

/// <summary>
/// Общий обработчик апдейтов для всех ботов мастеров.
/// Получает контекст бота (кто его владелец) и нужный TelegramBotClient.
/// </summary>
public class MasterBotUpdateHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MasterBotUpdateHandler> _logger;

    public MasterBotUpdateHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<MasterBotUpdateHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
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
                    await bot.SendMessage(
                        chatId: message.Chat.Id,
                        text: "Нажмите /start, чтобы открыть меню записи.",
                        cancellationToken: ct);
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
                await bot.SendMessage(chatId,
                    "📅 Запись на услугу\n\n" +
                    "Эта функция скоро будет доступна.\n" +
                    "Пока что для записи свяжитесь с мастером напрямую.",
                    cancellationToken: ct);
                break;

            case "client_menu":
                await SendClientMenuAsync(bot, chatId, master, ct);
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
}