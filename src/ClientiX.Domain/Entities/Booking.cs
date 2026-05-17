namespace ClientiX.Domain.Entities;

/// <summary>
/// Запись клиента к мастеру.
/// </summary>
public class Booking
{
    public long Id { get; set; }
    public long UserId { get; set; }            // мастер
    public long ClientTelegramId { get; set; }  // клиент мастера
    public string? ClientName { get; set; }
    public string? ClientPhone { get; set; }
    public long ServiceId { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string Status { get; set; } = "confirmed"; // confirmed | cancelled | completed | no_show
    public int PriceRub { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Service Service { get; set; } = null!;
}