using ClientiX.Domain.Entities;
using ClientiX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClientiX.Infrastructure.Bookings;

/// <summary>
/// Сервис генерации свободных слотов для записи к мастеру.
/// Учитывает: шаблон расписания, исключения на конкретные даты,
/// существующие активные записи.
/// </summary>
public class BookingSlotService
{
    private const int SlotStepMinutes = 15;
    private readonly ClientiXDbContext _db;

    public BookingSlotService(ClientiXDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Возвращает список доступных времён начала записи для конкретной услуги на конкретную дату.
    /// </summary>
    /// <param name="masterUserId">ID мастера (internal user.id).</param>
    /// <param name="serviceDurationMinutes">Длительность услуги в минутах.</param>
    /// <param name="date">Дата (UTC, время игнорируется).</param>
    /// <param name="now">Текущий момент UTC (для отсечения прошедших слотов сегодня).</param>
    public async Task<List<DateTime>> GetAvailableSlotsAsync(
        long masterUserId, int serviceDurationMinutes, DateTime date,
        DateTime now, CancellationToken ct)
    {
        date = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

        // 1. Получаем рабочее окно на дату
        var workWindow = await GetWorkWindowAsync(masterUserId, date, ct);
        if (workWindow is null) return new List<DateTime>(); // выходной

        var (windowStart, windowEnd) = workWindow.Value;

        // 2. Получаем активные брони на этот день
        var dayStart = date;
        var dayEnd = date.AddDays(1);

        var bookings = await _db.Bookings
            .Where(b => b.UserId == masterUserId
                     && b.StartsAt >= dayStart
                     && b.StartsAt < dayEnd
                     && (b.Status == "pending" || b.Status == "confirmed"))
            .Select(b => new { b.StartsAt, b.EndsAt })
            .OrderBy(b => b.StartsAt)
            .ToListAsync(ct);

        // 3. Генерируем кандидатов с шагом 15 минут
        var result = new List<DateTime>();
        var candidate = windowStart;

        while (true)
        {
            var candidateEnd = candidate.AddMinutes(serviceDurationMinutes);
            if (candidateEnd > windowEnd) break;

            // Не предлагаем времена в прошлом (с запасом 10 минут на размышление)
            if (candidate < now.AddMinutes(10))
            {
                candidate = candidate.AddMinutes(SlotStepMinutes);
                continue;
            }

            // Проверяем перекрытие с существующими бронями
            bool overlaps = bookings.Any(b =>
                candidate < b.EndsAt && candidateEnd > b.StartsAt);

            if (!overlaps) result.Add(candidate);

            candidate = candidate.AddMinutes(SlotStepMinutes);
        }

        return result;
    }

    /// <summary>
    /// Возвращает рабочее окно мастера на конкретную дату с учётом
    /// шаблона недели и исключений. null = выходной.
    /// </summary>
    private async Task<(DateTime Start, DateTime End)?> GetWorkWindowAsync(
        long masterUserId, DateTime date, CancellationToken ct)
    {
        // 1. Проверяем, есть ли исключение на эту дату
        var exception = await _db.WorkScheduleExceptions
            .FirstOrDefaultAsync(x => x.UserId == masterUserId && x.Date == date, ct);

        int fromMinutes, toMinutes;
        bool isWorking;

        if (exception is not null)
        {
            isWorking = exception.IsWorking;
            fromMinutes = exception.FromMinutes;
            toMinutes = exception.ToMinutes;
        }
        else
        {
            // 2. Берём из шаблона недели
            var dayOfWeek = (int)date.DayOfWeek;
            var template = await _db.WorkScheduleTemplates
                .FirstOrDefaultAsync(t => t.UserId == masterUserId && t.DayOfWeek == dayOfWeek, ct);

            if (template is null) return null; // расписание не настроено

            isWorking = template.IsWorking;
            fromMinutes = template.FromMinutes;
            toMinutes = template.ToMinutes;
        }

        if (!isWorking || toMinutes <= fromMinutes) return null;

        var start = date.AddMinutes(fromMinutes);
        var end = date.AddMinutes(toMinutes);

        return (start, end);
    }

    /// <summary>
    /// Сгенерировать список дат на ближайшие N дней с пометкой,
    /// есть ли хоть один свободный слот под услугу.
    /// </summary>
    public async Task<List<(DateTime Date, bool HasFreeSlot)>> GetDaysWithAvailabilityAsync(
        long masterUserId, int serviceDurationMinutes, int daysAhead,
        DateTime now, CancellationToken ct)
    {
        var result = new List<(DateTime, bool)>();
        var today = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);

        for (int i = 0; i < daysAhead; i++)
        {
            var date = today.AddDays(i);
            var slots = await GetAvailableSlotsAsync(masterUserId, serviceDurationMinutes, date, now, ct);
            result.Add((date, slots.Count > 0));
        }

        return result;
    }
}