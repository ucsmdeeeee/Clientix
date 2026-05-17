using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ClientiX.BotGateway;

/// <summary>
/// Фоновый сервис, опрашивающий Telegram Bot API через long polling.
/// На этапе разработки используется polling вместо webhook,
/// так как локальный сервер не доступен из интернета.
/// На продакшене будет заменён на webhook-обработчик.
/// </summary>
public class TelegramPollingService : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly ILogger<TelegramPollingService> _logger;

    public TelegramPollingService(
        ITelegramBotClient bot,
        ILogger<TelegramPollingService> logger)
    {
        _bot = bot;
        _logger = logger;
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

        // Держим сервис живым до отмены
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleUpdateAsync(
        ITelegramBotClient bot,
        Update update,
        CancellationToken ct)
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
                    await SendStartMenuAsync(bot, message.Chat.Id, ct);
                }
                else
                {
                    await bot.SendMessage(
                        chatId: message.Chat.Id,
                        text: "Я понимаю команду /start. Нажмите её, чтобы открыть меню.",
                        cancellationToken: ct);
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

    private async Task SendStartMenuAsync(
        ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        const string text =
            "👋 Добро пожаловать в *ClientiX*\\!\n\n" +
            "Платформа аренды Telegram\\-ботов для мастеров " +
            "бьюти\\-индустрии\\.\n\n" +
            "Что вы хотите сделать?";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("✨ Подключить своего бота", "connect_bot") },
            new[] { InlineKeyboardButton.WithCallbackData("💎 Тарифы и подписка", "tariffs") },
            new[] { InlineKeyboardButton.WithCallbackData("📊 Мой кабинет", "cabinet") },
            new[] { InlineKeyboardButton.WithCallbackData("ℹ️ О сервисе", "about") },
        });

        await bot.SendMessage(
            chatId: chatId,
            text: text,
            parseMode: ParseMode.MarkdownV2,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task HandleCallbackAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        var data = callback.Data ?? string.Empty;
        var chatId = callback.Message!.Chat.Id;

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
                "📊 Личный кабинет\n\n" +
                "Здесь будет статус подписки, ссылка на бота и статистика.\n" +
                "(Функция в разработке)",
            "about" =>
                "ℹ️ ClientiX — SaaS-платформа аренды Telegram-ботов " +
                "для самозанятых мастеров бьюти-индустрии.\n\n" +
                "Версия: 0.1 (alpha)",
            _ => "Команда не распознана."
        };

        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
        await bot.SendMessage(chatId, reply, cancellationToken: ct);
    }

    private Task HandleErrorAsync(
        ITelegramBotClient bot, Exception exception, CancellationToken ct)
    {
        _logger.LogError(exception, "Ошибка Telegram polling");
        return Task.CompletedTask;
    }
}