namespace ClientiX.Domain.Entities;

/// <summary>
/// Запись о том, что определённое напоминание для записи уже отправлено.
/// Предотвращает повторную отправку одного и того же напоминания.
/// </summary>
public class NotificationSent
{
    public long Id { get; set; }
    public long BookingId { get; set; }

    /// <summary>Тип напоминания: "24h" или "extra_Xh" (например "extra_3h").</summary>
    public string Kind { get; set; } = null!;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}