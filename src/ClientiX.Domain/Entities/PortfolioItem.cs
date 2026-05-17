namespace ClientiX.Domain.Entities;

/// <summary>
/// Элемент портфолио мастера (фото работы).
/// Хранится как telegram_file_id, без своего хранилища.
/// </summary>
public class PortfolioItem
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string TelegramFileId { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}