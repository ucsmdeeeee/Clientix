using ClientiX.Domain.Entities;
using ClientiX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClientiX.Infrastructure.Repositories;

/// <summary>
/// Репозиторий для работы с пользователями платформы.
/// </summary>
public class UserRepository
{
    private readonly ClientiXDbContext _db;

    public UserRepository(ClientiXDbContext db)
    {
        _db = db;
    }

    public async Task<User?> GetByTelegramIdAsync(long telegramId, CancellationToken ct)
    {
        return await _db.Users
            .Include(u => u.Subscription)
            .Include(u => u.ManagedBot)
            .FirstOrDefaultAsync(u => u.TelegramId == telegramId, ct);
    }

    /// <summary>
    /// Создаёт нового пользователя с триальной подпиской на 7 дней.
    /// </summary>
    public async Task<User> CreateMasterAsync(
        long telegramId,
        string? username,
        string? firstName,
        string? lastName,
        CancellationToken ct)
    {
        var referralCode = GenerateReferralCode();

        var user = new User
        {
            TelegramId = telegramId,
            TelegramUsername = username,
            FirstName = firstName,
            LastName = lastName,
            Role = "master",
            ReferralCode = referralCode,
            HasMadeFirstPayment = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        // Создаём триальную подписку на 7 дней
        var subscription = new Subscription
        {
            UserId = user.Id,
            Status = "trial",
            TrialEndsAt = DateTime.UtcNow.AddDays(7),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(7),
            AutoRenew = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Subscriptions.Add(subscription);
        await _db.SaveChangesAsync(ct);

        user.Subscription = subscription;
        return user;
    }

    /// <summary>
    /// Генерирует короткий реферальный код из 8 символов (буквы + цифры).
    /// </summary>
    private static string GenerateReferralCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // без похожих 0,O,1,I
        var random = Random.Shared;
        return new string(Enumerable.Range(0, 8)
            .Select(_ => chars[random.Next(chars.Length)])
            .ToArray());
    }

    /// <summary>
    /// Обновляет данные мастера после прохождения регистрационной анкеты.
    /// </summary>
    public async Task UpdateMasterProfileAsync(
        long telegramId,
        string niche,
        string city,
        string phone,
        CancellationToken ct)
    {
        var user = await _db.Users
            .Include(u => u.ManagedBot)
            .FirstOrDefaultAsync(u => u.TelegramId == telegramId, ct);

        if (user is null) return;

        user.Phone = phone;
        user.UpdatedAt = DateTime.UtcNow;

        // Ниша и город пока временно складываем в ManagedBot — там есть нужные поля.
        // Создадим заглушку ManagedBot, чтобы в неё записать профильные данные.
        if (user.ManagedBot is null)
        {
            user.ManagedBot = new ManagedBot
            {
                UserId = user.Id,
                BotTelegramId = 0, // пока бот не подключён
                BotUsername = "",
                BotTokenEncrypted = "",
                WebhookSecret = "",
                IsActive = false,
                Niche = niche,
                City = city,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.ManagedBots.Add(user.ManagedBot);
        }
        else
        {
            user.ManagedBot.Niche = niche;
            user.ManagedBot.City = city;
            user.ManagedBot.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }
}