using ClientiX.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ClientiX.WebApi.Controllers;

[ApiController]
[Route("api/master")]
[Authorize]
public class MasterController : ControllerBase
{
    private readonly UserRepository _users;
    private readonly ILogger<MasterController> _logger;

    public MasterController(UserRepository users, ILogger<MasterController> logger)
    {
        _users = users;
        _logger = logger;
    }

    private long GetCurrentUserId()
    {
        var sub = User.FindFirst("sub")?.Value;
        return long.TryParse(sub, out var id) ? id : 0;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == 0) return Unauthorized();

        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return NotFound();

        return Ok(new
        {
            id = user.Id,
            telegramId = user.TelegramId,
            firstName = user.FirstName,
            lastName = user.LastName,
            username = user.TelegramUsername,
            city = user.ManagedBot?.City,
            niche = user.ManagedBot?.Niche,
            botUsername = user.ManagedBot?.BotUsername,
            subscriptionStatus = user.Subscription?.Status,
            timezone = user.TimeZone,
            reminderDayBefore = user.ReminderDayBefore,
            reminderExtraHours = user.ReminderExtraHours
        });
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == 0) return Unauthorized();

        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return NotFound();

        var tz = user.TimeZone;
        var nowLocal = ClientiX.Infrastructure.TimeZones.NowInZone(tz);
        var todayStartLocal = nowLocal.Date;
        var monthStartLocal = todayStartLocal.AddDays(-29);
        var weekStartLocal = todayStartLocal.AddDays(-6);

        var todayStartUtc = ClientiX.Infrastructure.TimeZones.ZoneToUtc(todayStartLocal, tz);
        var todayEndUtc = ClientiX.Infrastructure.TimeZones.ZoneToUtc(todayStartLocal.AddDays(1), tz);
        var weekStartUtc = ClientiX.Infrastructure.TimeZones.ZoneToUtc(weekStartLocal, tz);
        var monthStartUtc = ClientiX.Infrastructure.TimeZones.ZoneToUtc(monthStartLocal, tz);

        var statsToday = await _users.GetStatsAsync(userId, todayStartUtc, todayEndUtc, ct);
        var statsWeek = await _users.GetStatsAsync(userId, weekStartUtc, todayEndUtc, ct);
        var statsMonth = await _users.GetStatsAsync(userId, monthStartUtc, todayEndUtc, ct);

        return Ok(new { today = statsToday, week = statsWeek, month = statsMonth });
    }
}