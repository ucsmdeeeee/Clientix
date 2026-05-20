using ClientiX.Infrastructure.Persistence;
using ClientiX.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClientiX.WebApi.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : ControllerBase
{
    private readonly ClientiXDbContext _db;
    private readonly UserRepository _users;

    public AdminController(ClientiXDbContext db, UserRepository users)
    {
        _db = db;
        _users = users;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(CancellationToken ct)
    {
        var totalMasters = await _db.Users.CountAsync(ct);
        var activeBots = await _db.ManagedBots.CountAsync(b => b.IsActive, ct);
        var paying = await _db.Subscriptions.CountAsync(s => s.Status == "active", ct);
        var trial = await _db.Subscriptions.CountAsync(s => s.Status == "trial", ct);

        var nowUtc = DateTime.UtcNow;
        var monthAgoUtc = nowUtc.AddDays(-30);
        var totalBookings30d = await _db.Bookings
            .CountAsync(b => b.CreatedAt >= monthAgoUtc, ct);

        var completedBookings30d = await _db.Bookings
            .CountAsync(b => b.Status == "completed" && b.CreatedAt >= monthAgoUtc, ct);

        var revenue30d = await _db.Bookings
            .Where(b => b.Status == "completed" && b.CreatedAt >= monthAgoUtc)
            .SumAsync(b => (int?)b.PriceRub, ct) ?? 0;

        return Ok(new
        {
            totalMasters,
            activeBots,
            paying,
            trial,
            totalBookings30d,
            completedBookings30d,
            revenue30d
        });
    }

    [HttpGet("masters")]
    public async Task<IActionResult> GetMasters(CancellationToken ct)
    {
        var list = await _db.Users
            .Include(u => u.ManagedBot)
            .Include(u => u.Subscription)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new
            {
                id = u.Id,
                telegramId = u.TelegramId,
                firstName = u.FirstName,
                username = u.TelegramUsername,
                city = u.ManagedBot != null ? u.ManagedBot.City : null,
                niche = u.ManagedBot != null ? u.ManagedBot.Niche : null,
                botUsername = u.ManagedBot != null ? u.ManagedBot.BotUsername : null,
                isActive = u.ManagedBot != null && u.ManagedBot.IsActive,
                subscriptionStatus = u.Subscription != null ? u.Subscription.Status : "none",
                createdAt = u.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(list);
    }
}