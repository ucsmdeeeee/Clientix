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
    DateTime now, string masterTimeZone, CancellationToken ct)
    {
        var tz = TimeZones.Get(masterTimeZone);

        // date — это календарный день в зоне мастера. Берём его как полночь в этой зоне.
        var dateLocal = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);

        var workWindow = await GetWorkWindowAsync(masterUserId, dateLocal, ct);
        if (workWindow is null) return new List<DateTime>();

        var (windowStartLocal, windowEndLocal) = workWindow.Value;
        var windowStart = TimeZoneInfo.ConvertTimeToUtc(windowStartLocal, tz);
        var windowEnd = TimeZoneInfo.ConvertTimeToUtc(windowEndLocal, tz);

        var dayStart = TimeZoneInfo.ConvertTimeToUtc(dateLocal, tz);
        var dayEnd = TimeZoneInfo.ConvertTimeToUtc(dateLocal.AddDays(1), tz);

        var bookings = await _db.Bookings
            .Where(b => b.UserId == masterUserId
                     && b.StartsAt >= dayStart
                     && b.StartsAt < dayEnd
                     && (b.Status == "pending" || b.Status == "confirmed"))
            .Select(b => new { b.StartsAt, b.EndsAt })
            .OrderBy(b => b.StartsAt)
            .ToListAsync(ct);

        var result = new List<DateTime>();
        var candidate = windowStart;

        while (true)
        {
            var candidateEnd = candidate.AddMinutes(serviceDurationMinutes);
            if (candidateEnd > windowEnd) break;

            if (candidate < now.AddMinutes(10))
            {
                candidate = candidate.AddMinutes(SlotStepMinutes);
                continue;
            }

            bool overlaps = bookings.Any(b => candidate < b.EndsAt && candidateEnd > b.StartsAt);
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
    long masterUserId, DateTime dateLocal, CancellationToken ct)
    {
        // dateLocal — полночь в зоне мастера (Unspecified).
        // В БД исключения хранятся как date (UTC midnight). Для сравнения берём такую же.
        var dateAsUtc = DateTime.SpecifyKind(dateLocal.Date, DateTimeKind.Utc);

        var exception = await _db.WorkScheduleExceptions
            .FirstOrDefaultAsync(x => x.UserId == masterUserId && x.Date == dateAsUtc, ct);

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
            var dayOfWeek = (int)dateLocal.DayOfWeek;
            var template = await _db.WorkScheduleTemplates
                .FirstOrDefaultAsync(t => t.UserId == masterUserId && t.DayOfWeek == dayOfWeek, ct);

            if (template is null) return null;

            isWorking = template.IsWorking;
            fromMinutes = template.FromMinutes;
            toMinutes = template.ToMinutes;
        }

        if (!isWorking || toMinutes <= fromMinutes) return null;

        var start = dateLocal.AddMinutes(fromMinutes);
        var end = dateLocal.AddMinutes(toMinutes);

        return (start, end);
    }

    /// <summary>
    /// Сгенерировать список дат на ближайшие N дней с пометкой,
    /// есть ли хоть один свободный слот под услугу.
    /// </summary>
    public async Task<List<(DateTime Date, bool HasFreeSlot)>> GetDaysWithAvailabilityAsync(
    long masterUserId, int serviceDurationMinutes, int daysAhead,
    DateTime now, string masterTimeZone, CancellationToken ct)
    {
        var result = new List<(DateTime, bool)>();
        var todayLocal = TimeZones.NowInZone(masterTimeZone).Date;

        for (int i = 0; i < daysAhead; i++)
        {
            var date = todayLocal.AddDays(i);
            var slots = await GetAvailableSlotsAsync(masterUserId, serviceDurationMinutes, date, now, masterTimeZone, ct);
            result.Add((date, slots.Count > 0));
        }

        return result;
    }

    /// <summary>
    /// Возвращает услуги мастера, которые помещаются сразу после указанного времени окончания
    /// и до конца рабочего окна на эту дату, и до начала следующей брони (если есть).
    /// </summary>
    public async Task<List<long>> GetServicesFittingAfterAsync(
        long masterUserId, DateTime endsAt, string masterTimeZone, CancellationToken ct)
    {
        var endsLocal = TimeZoneInfo.ConvertTimeFromUtc(endsAt, TimeZones.Get(masterTimeZone));
        var dateLocal = DateTime.SpecifyKind(endsLocal.Date, DateTimeKind.Unspecified);

        var workWindow = await GetWorkWindowAsync(masterUserId, dateLocal, ct);
        if (workWindow is null) return new();

        var (_, windowEndLocal) = workWindow.Value;
        var windowEndUtc = TimeZoneInfo.ConvertTimeToUtc(windowEndLocal, TimeZones.Get(masterTimeZone));

        // Берём следующую активную бронь после нашей
        var nextBookingStart = await _db.Bookings
            .Where(b => b.UserId == masterUserId
                     && b.StartsAt > endsAt
                     && (b.Status == "pending" || b.Status == "confirmed"))
            .OrderBy(b => b.StartsAt)
            .Select(b => (DateTime?)b.StartsAt)
            .FirstOrDefaultAsync(ct);

        var availableUntil = nextBookingStart ?? windowEndUtc;
        if (nextBookingStart is not null && nextBookingStart > windowEndUtc) availableUntil = windowEndUtc;

        var availableMinutes = (int)Math.Floor((availableUntil - endsAt).TotalMinutes);
        if (availableMinutes < 15) return new();

        var fittingIds = await _db.Services
            .Where(s => s.UserId == masterUserId && s.DurationMinutes <= availableMinutes)
            .Select(s => s.Id)
            .ToListAsync(ct);

        return fittingIds;
    }
}