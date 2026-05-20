using ClientiX.WebApi.DTOs;
using System.Security.Cryptography;
using System.Text;

namespace ClientiX.WebApi.Services;

public class TelegramAuthService
{
    private readonly IConfiguration _config;
    private readonly ILogger<TelegramAuthService> _logger;

    public TelegramAuthService(IConfiguration config, ILogger<TelegramAuthService> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Проверяет подпись Telegram Login Widget по схеме из официальной документации:
    /// https://core.telegram.org/widgets/login#checking-authorization
    /// </summary>
    public bool VerifyLoginData(TelegramLoginDto data)
    {
        var botToken = _config["Telegram:MainBotToken"];
        if (string.IsNullOrEmpty(botToken))
        {
            _logger.LogError("Telegram:MainBotToken не настроен");
            return false;
        }

        // 1. Собираем data_check_string по полям, отсортированным по имени, без поля hash
        var fields = new SortedDictionary<string, string?>
        {
            ["auth_date"] = data.Auth_date.ToString(),
            ["first_name"] = data.First_name,
            ["id"] = data.Id.ToString(),
            ["last_name"] = data.Last_name,
            ["photo_url"] = data.Photo_url,
            ["username"] = data.Username
        };

        var dataCheckString = string.Join('\n',
            fields
                .Where(kv => !string.IsNullOrEmpty(kv.Value))
                .Select(kv => $"{kv.Key}={kv.Value}"));

        // 2. secret_key = SHA256(bot_token)
        using var sha256 = SHA256.Create();
        var secretKey = sha256.ComputeHash(Encoding.UTF8.GetBytes(botToken));

        // 3. expected_hash = HMAC-SHA256(secret_key, data_check_string)
        using var hmac = new HMACSHA256(secretKey);
        var expectedHashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString));
        var expectedHash = Convert.ToHexString(expectedHashBytes).ToLowerInvariant();

        // 4. Сравниваем (constant-time)
        var providedHash = (data.Hash ?? "").ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedHash),
            Encoding.UTF8.GetBytes(providedHash)))
        {
            _logger.LogWarning("Telegram Login: подпись не совпадает для user={UserId}", data.Id);
            return false;
        }

        // 5. Проверяем что auth_date не старше 24 часов (защита от replay)
        var authTime = DateTimeOffset.FromUnixTimeSeconds(data.Auth_date);
        if (DateTimeOffset.UtcNow - authTime > TimeSpan.FromHours(24))
        {
            _logger.LogWarning("Telegram Login: устаревшая подпись от user={UserId}", data.Id);
            return false;
        }

        return true;
    }
}