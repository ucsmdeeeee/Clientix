using Telegram.Bot;

namespace ClientiX.BotGateway.MasterBots;

/// <summary>
/// Информация о запущенном боте мастера: клиент, владелец, источник отмены.
/// Хранится в MasterBotManager.
/// </summary>
public class MasterBotContext
{
    public required long UserId { get; init; }           // мастер (наш user.id, не telegram_id)
    public required long BotTelegramId { get; init; }     // id бота в телеграме
    public required string BotUsername { get; init; }
    public required ITelegramBotClient Client { get; init; }
    public required CancellationTokenSource Cts { get; init; }
}