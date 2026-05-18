using ClientiX.Infrastructure.Repositories;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
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
                // TODO: обработка кнопок в следующем подэтапе
                await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
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
}