using ClientiX.WebApi.DTOs;
using ClientiX.WebApi.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace ClientiX.Tests.Services;

/// <summary>
/// Юнит-тесты для <see cref="TelegramAuthService"/>.
/// Проверяют корректность реализации алгоритма проверки подписи Telegram Login Widget
/// (HMAC-SHA256 от data_check_string с ключом SHA256(bot_token)).
/// Тестируем три сценария: валидная подпись, инвалидная подпись, устаревший auth_date,
/// а также защиту от tampering (подделки полей при сохранённой подписи).
/// </summary>
public class TelegramAuthServiceTests
{
    // Тестовый токен бота — не настоящий, только для расчётов HMAC в тестах
    private const string TestBotToken = "1234567890:ABCDEF_test_bot_token_for_unit_tests_only";

    /// <summary>
    /// Фабрика — создаёт сервис с тестовой конфигурацией.
    /// </summary>
    private static TelegramAuthService CreateService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:MainBotToken"] = TestBotToken
            })
            .Build();

        return new TelegramAuthService(config, NullLogger<TelegramAuthService>.Instance);
    }

    /// <summary>
    /// Хелпер — вычисляет правильный hash для данных Login Widget
    /// по алгоритму из официальной документации Telegram.
    /// Копия логики из TelegramAuthService.VerifyLoginData, нужная чтобы в тестах
    /// можно было сгенерировать заведомо валидную подпись.
    /// </summary>
    private static string ComputeValidHash(TelegramLoginDto data, string botToken)
    {
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
            fields.Where(kv => !string.IsNullOrEmpty(kv.Value))
                  .Select(kv => $"{kv.Key}={kv.Value}"));

        using var sha256 = SHA256.Create();
        var secretKey = sha256.ComputeHash(Encoding.UTF8.GetBytes(botToken));

        using var hmac = new HMACSHA256(secretKey);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Happy path: при корректно вычисленной подписи и свежем auth_date
    /// сервис должен признать данные валидными и вернуть true.
    /// </summary>
    [Fact]
    public void VerifyLoginData_With_Valid_Signature_Returns_True()
    {
        var service = CreateService();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var data = new TelegramLoginDto
        {
            Id = 12345,
            First_name = "Test",
            Username = "test_user",
            Auth_date = now,
            Hash = ""
        };

        var validHash = ComputeValidHash(data, TestBotToken);
        var dataWithHash = data with { Hash = validHash };

        var result = service.VerifyLoginData(dataWithHash);

        result.Should().BeTrue();
    }

    /// <summary>
    /// Защита от мусорных запросов: если в данных стоит произвольный hash,
    /// сервис должен отклонить попытку входа.
    /// </summary>
    [Fact]
    public void VerifyLoginData_With_Invalid_Signature_Returns_False()
    {
        var service = CreateService();
        var data = new TelegramLoginDto
        {
            Id = 12345,
            First_name = "Test",
            Auth_date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Hash = "invalid_hash_that_does_not_match_anything_at_all_0000"
        };

        var result = service.VerifyLoginData(data);

        result.Should().BeFalse();
    }

    /// <summary>
    /// Защита от replay-атаки: даже с корректной подписью, если auth_date старше 24 часов,
    /// сервис должен отклонить попытку входа (по требованиям Telegram).
    /// </summary>
    [Fact]
    public void VerifyLoginData_With_Expired_AuthDate_Returns_False()
    {
        var service = CreateService();
        // auth_date 25 часов назад — старше 24-часового окна
        var expiredTime = DateTimeOffset.UtcNow.AddHours(-25).ToUnixTimeSeconds();

        var data = new TelegramLoginDto
        {
            Id = 12345,
            First_name = "Test",
            Auth_date = expiredTime,
            Hash = ""
        };

        var validHash = ComputeValidHash(data, TestBotToken);
        var dataWithHash = data with { Hash = validHash };

        var result = service.VerifyLoginData(dataWithHash);

        result.Should().BeFalse();
    }

    /// <summary>
    /// Защита от tampering (подмены полей):
    /// если злоумышленник возьмёт чужой валидный hash и попытается прицепить его
    /// к данным с другим именем пользователя — подпись должна не сойтись и сервис вернёт false.
    /// </summary>
    [Fact]
    public void VerifyLoginData_With_Tampered_FirstName_Returns_False()
    {
        var service = CreateService();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var originalData = new TelegramLoginDto
        {
            Id = 12345,
            First_name = "Original",
            Auth_date = now,
            Hash = ""
        };

        var hash = ComputeValidHash(originalData, TestBotToken);

        // Подменяем имя но используем оригинальный hash
        var tamperedData = originalData with { First_name = "Hacker", Hash = hash };

        var result = service.VerifyLoginData(tamperedData);

        result.Should().BeFalse();
    }
}