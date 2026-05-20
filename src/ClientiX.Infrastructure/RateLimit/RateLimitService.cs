using StackExchange.Redis;
using System.Threading.Tasks;
using System;

namespace ClientiX.Infrastructure.RateLimit;

/// <summary>
/// Простой rate-limit на базе Redis. INCR + TTL.
/// Если счётчик превышен — возвращает false, действие запрещено.
/// </summary>
public class RateLimitService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RateLimitService> _logger;

    public RateLimitService(IConnectionMultiplexer redis, ILogger<RateLimitService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Проверяет лимит и атомарно увеличивает счётчик.
    /// </summary>
    /// <param name="key">Уникальный ключ (например "book:7188...")</param>
    /// <param name="maxRequests">Сколько действий разрешено за окно</param>
    /// <param name="windowSeconds">Размер окна в секундах</param>
    /// <returns>true если разрешено, false если превышено</returns>
    public async Task<bool> TryAcquireAsync(string key, int maxRequests, int windowSeconds)
    {
        try
        {
            var db = _redis.GetDatabase();
            var redisKey = $"rl:{key}";
            var count = await db.StringIncrementAsync(redisKey);
            if (count == 1)
            {
                // Первое обращение — ставим TTL
                await db.KeyExpireAsync(redisKey, TimeSpan.FromSeconds(windowSeconds));
            }

            return count <= maxRequests;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RateLimit: ошибка проверки лимита для ключа {Key}, пропускаю", key);
            // Если Redis недоступен — не блокируем легитимных пользователей
            return true;
        }
    }
}