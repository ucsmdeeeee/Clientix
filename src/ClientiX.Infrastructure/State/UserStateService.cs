using System.Text.Json;
using StackExchange.Redis;

namespace ClientiX.Infrastructure.State;

/// <summary>
/// Состояние пользователя в FSM (конечном автомате) диалога Telegram-бота.
/// Хранится в Redis с автоматическим истечением через 30 минут бездействия.
/// </summary>
public class UserState
{
    public string CurrentStep { get; set; } = "";  // niche | city | phone | done
    public Dictionary<string, string> Data { get; set; } = new();
}

/// <summary>
/// Сервис управления состоянием FSM пользователей через Redis.
/// </summary>
public class UserStateService
{
    private readonly IConnectionMultiplexer _redis;
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(30);

    public UserStateService(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<UserState?> GetAsync(long telegramId)
    {
        var db = _redis.GetDatabase();
        var json = await db.StringGetAsync(Key(telegramId));
        if (json.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<UserState>(json!);
    }

    public async Task SetAsync(long telegramId, UserState state)
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(state);
        await db.StringSetAsync(Key(telegramId), json, StateLifetime);
    }

    public async Task ClearAsync(long telegramId)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(Key(telegramId));
    }

    private static string Key(long telegramId) => $"fsm:user:{telegramId}";
}