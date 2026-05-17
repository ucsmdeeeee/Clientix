namespace ClientiX.Domain.Entities;

/// <summary>
/// Telegram-бот мастера, арендованный через платформу ClientiX.
/// Токен хранится в зашифрованном виде.
/// </summary>
public class ManagedBot
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long BotTelegramId { get; set; }
    public string BotUsername { get; set; } = string.Empty;
    public string BotTokenEncrypted { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? Niche { get; set; } // beauty | tattoo | barber | nails
    public string? DisplayName { get; set; }
    public string? City { get; set; }
    public string? About { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}