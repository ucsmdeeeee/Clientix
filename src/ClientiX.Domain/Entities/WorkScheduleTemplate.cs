namespace ClientiX.Domain.Entities;

/// <summary>
/// Шаблон рабочего расписания мастера на неделю.
/// На каждого мастера должно быть 7 записей — по одной на каждый день недели.
/// Сюда смотрит алгоритм расчёта свободных слотов в первую очередь.
/// </summary>
public class WorkScheduleTemplate
{
    public long Id { get; set; }
    public long UserId { get; set; }

    /// <summary>
    /// День недели: 0 = воскресенье, 1 = понедельник, ..., 6 = суббота.
    /// Используем стандарт .NET DayOfWeek.
    /// </summary>
    public int DayOfWeek { get; set; }

    public bool IsWorking { get; set; }

    /// <summary>Время начала работы (минут от полуночи, например 600 = 10:00).</summary>
    public int FromMinutes { get; set; }

    /// <summary>Время окончания работы (минут от полуночи, например 1200 = 20:00).</summary>
    public int ToMinutes { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}