using ClientiX.Domain.Entities;
using ClientiX.Infrastructure.Persistence;
using ClientiX.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;

namespace ClientiX.BotGateway;

/// <summary>
/// Фоновый сервис, который каждые 5 минут проверяет какие напоминания
/// пора отправить и отправляет их клиентам через бот мастера.
/// </summary>
public class ReminderHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReminderHostedService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public ReminderHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<ReminderHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReminderHostedService запущен (интервал {Interval})", _interval);

        // Небольшая задержка при старте, чтобы основные сервисы успели подняться
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndSendRemindersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в цикле напоминаний");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task CheckAndSendRemindersAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClientiXDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var manager = scope.ServiceProvider
            .GetRequiredService<ClientiX.BotGateway.MasterBots.MasterBotManager>();

        var now = DateTime.UtcNow;
        var horizonEnd = now.AddHours(48); // смотрим вперёд на 48 часов

        // Берём активные записи на ближайшие 48 часов
        var upcomingBookings = await db.Bookings
            .Include(b => b.Service)
            .Include(b => b.User)
            .Where(b => b.StartsAt >= now
                     && b.StartsAt <= horizonEnd
                     && (b.Status == "pending" || b.Status == "confirmed"))
            .ToListAsync(ct);

        if (upcomingBookings.Count == 0) return;

        var bookingIds = upcomingBookings.Select(b => b.Id).ToList();
        var alreadySent = await db.NotificationsSent
            .Where(n => bookingIds.Contains(n.BookingId))
            .ToListAsync(ct);

        foreach (var booking in upcomingBookings)
        {
            var master = booking.User;
            var hoursUntil = (booking.StartsAt - now).TotalHours;

            // Напоминание за 24 часа: окно 23.0–25.0 часа
            if (master.ReminderDayBefore && hoursUntil >= 23.0 && hoursUntil <= 25.0)
            {
                if (!alreadySent.Any(n => n.BookingId == booking.Id && n.Kind == "24h"))
                {
                    await SendReminderAsync(manager, users, db, booking, "24h", "за 24 часа", ct);
                }
            }

            // Дополнительное напоминание
            if (master.ReminderExtraHours.HasValue)
            {
                var h = master.ReminderExtraHours.Value;
                var kind = $"extra_{h}h";
                // Окно: ±10 минут от целевого времени
                if (hoursUntil >= h - 0.16 && hoursUntil <= h + 0.16)
                {
                    if (!alreadySent.Any(n => n.BookingId == booking.Id && n.Kind == kind))
                    {
                        await SendReminderAsync(manager, users, db, booking, kind, $"за {h} час(ов)", ct);
                    }
                }
            }
        }
    }

    private async Task SendReminderAsync(
        ClientiX.BotGateway.MasterBots.MasterBotManager manager,
        UserRepository users,
        ClientiXDbContext db,
        Booking booking,
        string kind,
        string label,
        CancellationToken ct)
    {
        try
        {
            if (!manager.ActiveBots.TryGetValue(booking.UserId, out var ctx))
            {
                _logger.LogWarning("Бот мастера {UserId} не запущен, не могу отправить напоминание", booking.UserId);
                return;
            }

            var tz = booking.User.TimeZone;
            var startLocal = ClientiX.Infrastructure.TimeZones.ToZone(booking.StartsAt, tz);
            var allServices = await users.GetServicesAsync(booking.UserId, ct);

            // Формируем список услуг
            var serviceNames = new List<string> { booking.Service.Name };
            if (!string.IsNullOrEmpty(booking.AdditionalServiceIds))
            {
                foreach (var idStr in booking.AdditionalServiceIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!long.TryParse(idStr, out var id)) continue;
                    var svc = allServices.FirstOrDefault(s => s.Id == id);
                    if (svc is not null) serviceNames.Add(svc.Name);
                }
            }
            var fullList = string.Join(" + ", serviceNames);

            var text = $"🔔 Напоминание ({label} до записи)\n\n" +
                       $"📅 {startLocal:dddd, dd MMMM} в {startLocal:HH:mm}\n" +
                       $"💼 {fullList}\n" +
                       $"⏱ {booking.DurationMinutes} мин · 💰 {booking.PriceRub} ₽\n\n" +
                       "Ждём вас! Если планы изменились — отмените запись в боте.";

            await ctx.Client.SendMessage(booking.ClientTelegramId, text, cancellationToken: ct);

            db.NotificationsSent.Add(new NotificationSent
            {
                BookingId = booking.Id,
                Kind = kind,
                SentAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Отправлено напоминание {Kind} booking={BookingId} клиенту {ClientTgId}",
                kind, booking.Id, booking.ClientTelegramId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось отправить напоминание booking={BookingId} kind={Kind}",
                booking.Id, kind);
        }
    }
}