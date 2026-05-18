namespace ClientiX.Domain.Entities;

/// <summary>
/// Исключение в расписании мастера на конкретную дату.
/// Перебивает шаблон недели. Например: "28 декабря не работаю" или
/// "3 января с 14:00 до 18:00".
/// </summary>
public class WorkScheduleException
{
    public long Id { get; set; }
    public long UserId { get; set; }

    /// <summary>Дата исключения (без времени, UTC).</summary>
    public DateTime Date { get; set; }

    public bool IsWorking { get; set; }

    public int FromMinutes { get; set; }
    public int ToMinutes { get; set; }

    /// <summary>Комментарий мастера (необязательный).</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}