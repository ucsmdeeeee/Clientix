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

    /// <summary>
    /// Получает все активные услуги мастера.
    /// </summary>
    public async Task<List<Service>> GetServicesAsync(long userId, CancellationToken ct)
    {
        return await _db.Services
            .Where(s => s.UserId == userId && s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Добавляет новую услугу мастеру.
    /// </summary>
    public async Task<Service> AddServiceAsync(
        long userId, string name, int durationMinutes, int priceRub, CancellationToken ct)
    {
        var maxSort = await _db.Services
            .Where(s => s.UserId == userId)
            .Select(s => (int?)s.SortOrder)
            .MaxAsync(ct) ?? 0;

        var service = new Service
        {
            UserId = userId,
            Name = name,
            DurationMinutes = durationMinutes,
            PriceRub = priceRub,
            BufferAfterMinutes = 0,
            IsActive = true,
            SortOrder = maxSort + 1,
            CreatedAt = DateTime.UtcNow
        };

        _db.Services.Add(service);
        await _db.SaveChangesAsync(ct);
        return service;
    }

    /// <summary>
    /// Мягко удаляет услугу (помечает как неактивную).
    /// </summary>
    public async Task<bool> SoftDeleteServiceAsync(long userId, long serviceId, CancellationToken ct)
    {
        var service = await _db.Services
            .FirstOrDefaultAsync(s => s.Id == serviceId && s.UserId == userId, ct);
        if (service is null) return false;

        service.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Привязывает Telegram-бота мастера. Если у мастера уже есть подключённый бот — заменяет данные.
    /// </summary>
    public async Task AttachBotAsync(
        long userId,
        long botTelegramId,
        string botUsername,
        string encryptedToken,
        string webhookSecret,
        CancellationToken ct)
    {
        var user = await _db.Users
            .Include(u => u.ManagedBot)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return;

        if (user.ManagedBot is null)
        {
            user.ManagedBot = new ManagedBot
            {
                UserId = user.Id,
                BotTelegramId = botTelegramId,
                BotUsername = botUsername,
                BotTokenEncrypted = encryptedToken,
                WebhookSecret = webhookSecret,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.ManagedBots.Add(user.ManagedBot);
        }
        else
        {
            user.ManagedBot.BotTelegramId = botTelegramId;
            user.ManagedBot.BotUsername = botUsername;
            user.ManagedBot.BotTokenEncrypted = encryptedToken;
            user.ManagedBot.WebhookSecret = webhookSecret;
            user.ManagedBot.IsActive = true;
            user.ManagedBot.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Проверка, что другой мастер уже не подключил тот же бот.
    /// </summary>
    public async Task<bool> IsBotAlreadyAttachedAsync(
        long botTelegramId, long excludeUserId, CancellationToken ct)
    {
        return await _db.ManagedBots
            .AnyAsync(b => b.BotTelegramId == botTelegramId && b.UserId != excludeUserId, ct);
    }

    /// <summary>
    /// Возвращает шаблон расписания на неделю (7 дней).
    /// Если ни одного дня ещё не задано — создаёт дефолтное расписание
    /// (10:00–20:00 пн-пт, выходные сб-вс).
    /// </summary>
    public async Task<List<WorkScheduleTemplate>> GetWeeklyScheduleAsync(
        long userId, CancellationToken ct)
    {
        var existing = await _db.WorkScheduleTemplates
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.DayOfWeek)
            .ToListAsync(ct);

        if (existing.Count == 7) return existing;

        // Дополняем недостающие дни дефолтными значениями
        var existingDays = existing.Select(x => x.DayOfWeek).ToHashSet();
        for (int day = 0; day < 7; day++)
        {
            if (existingDays.Contains(day)) continue;

            bool isWeekday = day >= 1 && day <= 5; // пн-пт
            var newDay = new WorkScheduleTemplate
            {
                UserId = userId,
                DayOfWeek = day,
                IsWorking = isWeekday,
                FromMinutes = isWeekday ? 600 : 0,   // 10:00
                ToMinutes = isWeekday ? 1200 : 0,    // 20:00
                UpdatedAt = DateTime.UtcNow
            };
            _db.WorkScheduleTemplates.Add(newDay);
            existing.Add(newDay);
        }

        await _db.SaveChangesAsync(ct);
        return existing.OrderBy(x => x.DayOfWeek).ToList();
    }

    /// <summary>
    /// Обновляет один день расписания.
    /// </summary>
    public async Task SetWeeklyDayAsync(
        long userId, int dayOfWeek, bool isWorking, int fromMinutes, int toMinutes,
        CancellationToken ct)
    {
        var day = await _db.WorkScheduleTemplates
            .FirstOrDefaultAsync(x => x.UserId == userId && x.DayOfWeek == dayOfWeek, ct);

        if (day is null)
        {
            day = new WorkScheduleTemplate
            {
                UserId = userId,
                DayOfWeek = dayOfWeek,
                IsWorking = isWorking,
                FromMinutes = fromMinutes,
                ToMinutes = toMinutes,
                UpdatedAt = DateTime.UtcNow
            };
            _db.WorkScheduleTemplates.Add(day);
        }
        else
        {
            day.IsWorking = isWorking;
            day.FromMinutes = fromMinutes;
            day.ToMinutes = toMinutes;
            day.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Список ближайших исключений в расписании (на 60 дней вперёд).
    /// </summary>
    public async Task<List<WorkScheduleException>> GetUpcomingExceptionsAsync(
        long userId, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        return await _db.WorkScheduleExceptions
            .Where(x => x.UserId == userId && x.Date >= today && x.Date <= today.AddDays(60))
            .OrderBy(x => x.Date)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Добавить исключение на конкретную дату (или обновить существующее).
    /// </summary>
    public async Task UpsertExceptionAsync(
        long userId, DateTime date, bool isWorking, int fromMinutes, int toMinutes, string? note,
        CancellationToken ct)
    {
        date = date.Date; // отрезаем время

        var existing = await _db.WorkScheduleExceptions
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Date == date, ct);

        if (existing is null)
        {
            _db.WorkScheduleExceptions.Add(new WorkScheduleException
            {
                UserId = userId,
                Date = date,
                IsWorking = isWorking,
                FromMinutes = fromMinutes,
                ToMinutes = toMinutes,
                Note = note,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.IsWorking = isWorking;
            existing.FromMinutes = fromMinutes;
            existing.ToMinutes = toMinutes;
            existing.Note = note;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteExceptionAsync(long userId, long exceptionId, CancellationToken ct)
    {
        var ex = await _db.WorkScheduleExceptions
            .FirstOrDefaultAsync(x => x.Id == exceptionId && x.UserId == userId, ct);
        if (ex is null) return false;

        _db.WorkScheduleExceptions.Remove(ex);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}