using ClientiX.WebApi.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Xunit;

namespace ClientiX.Tests.Services;

/// <summary>
/// Юнит-тесты для <see cref="JwtService"/>.
/// Проверяют генерацию долгоживущих (30 дней) и короткоживущих (5 минут) JWT-токенов:
/// что токен формируется корректным, содержит нужные claims (sub, tg_id, is_admin, type),
/// имеет правильное время жизни, проходит валидацию с правильным секретом
/// и отклоняется при попытке подделки.
/// </summary>
public class JwtServiceTests
{
    // 256-битный секрет специально для тестов (не используется в проде)
    private const string TestSecret = "ThisIsATestSecretKeyForJwtServiceUnitTests1234567890";
    private const string TestIssuer = "test-issuer";
    private const string TestAudience = "test-audience";

    /// <summary>
    /// Фабрика — создаёт экземпляр JwtService с тестовой конфигурацией.
    /// Используется в каждом тесте этого класса.
    /// </summary>
    private static JwtService CreateService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = TestSecret,
                ["Jwt:Issuer"] = TestIssuer,
                ["Jwt:Audience"] = TestAudience
            })
            .Build();

        return new JwtService(config);
    }

    /// <summary>
    /// Проверяет, что метод возвращает непустую строку формата JWT (три части через точку).
    /// Это базовая sanity-проверка, что генератор вообще работает.
    /// </summary>
    [Fact]
    public void GenerateToken_Returns_Valid_Jwt_String()
    {
        var jwt = CreateService();

        var token = jwt.GenerateToken(userId: 42, telegramId: 100500, isAdmin: false);

        token.Should().NotBeNullOrEmpty();
        // JWT всегда состоит из 3 частей разделённых точкой (header.payload.signature)
        token.Split('.').Should().HaveCount(3);
    }

    /// <summary>
    /// Проверяет, что в payload токена есть все обязательные claims с правильными значениями:
    /// sub (id пользователя), tg_id (Telegram ID), is_admin (флаг администратора),
    /// а также корректные issuer и audience.
    /// </summary>
    [Fact]
    public void GenerateToken_Contains_Expected_Claims()
    {
        var jwt = CreateService();
        var token = jwt.GenerateToken(userId: 42, telegramId: 100500, isAdmin: true);

        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear(); // отключаем автомаппинг "sub" → "nameid"
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.Claims.Should().Contain(c => c.Type == "sub" && c.Value == "42");
        jwtToken.Claims.Should().Contain(c => c.Type == "tg_id" && c.Value == "100500");
        jwtToken.Claims.Should().Contain(c => c.Type == "is_admin" && c.Value == "true");
        jwtToken.Issuer.Should().Be(TestIssuer);
        jwtToken.Audiences.Should().Contain(TestAudience);
    }

    /// <summary>
    /// Проверяет, что долгоживущий токен истекает ровно через 30 дней от момента генерации
    /// (с допуском в 1 минуту на время выполнения теста).
    /// </summary>
    [Fact]
    public void GenerateToken_Expires_In_30_Days()
    {
        var jwt = CreateService();
        var beforeUtc = DateTime.UtcNow;

        var token = jwt.GenerateToken(userId: 1, telegramId: 1, isAdmin: false);

        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();
        var jwtToken = handler.ReadJwtToken(token);

        var expectedExpiry = beforeUtc.AddDays(30);
        jwtToken.ValidTo.Should().BeCloseTo(expectedExpiry, TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Проверяет короткоживущий токен (используется в deep-link авторизации через бота):
    /// должен истекать через 5 минут и содержать специальный claim type=web_login,
    /// чтобы его нельзя было использовать как обычный access-token.
    /// </summary>
    [Fact]
    public void GenerateShortLivedToken_Expires_In_5_Minutes_And_Has_Web_Login_Type()
    {
        var jwt = CreateService();
        var beforeUtc = DateTime.UtcNow;

        var token = jwt.GenerateShortLivedToken(userId: 7, telegramId: 200, isAdmin: false);

        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.ValidTo.Should().BeCloseTo(beforeUtc.AddMinutes(5), TimeSpan.FromSeconds(30));
        jwtToken.Claims.Should().Contain(c => c.Type == "type" && c.Value == "web_login");
    }

    /// <summary>
    /// Проверяет, что валидный токен успешно проходит проверку подписи и claims
    /// с тем же секретом и параметрами, что использовались при генерации.
    /// </summary>
    [Fact]
    public void GeneratedToken_Validates_Successfully_With_Same_Secret()
    {
        var jwt = CreateService();
        var token = jwt.GenerateToken(userId: 1, telegramId: 1, isAdmin: false);

        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));

        var validation = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = TestIssuer,
            ValidAudience = TestAudience,
            IssuerSigningKey = key
        };

        var act = () => handler.ValidateToken(token, validation, out _);
        act.Should().NotThrow();
    }

    /// <summary>
    /// Проверяет защиту от подделки: токен, подписанный одним секретом,
    /// не должен проходить валидацию с другим секретом.
    /// Это базовая гарантия криптографической стойкости JWT.
    /// </summary>
    [Fact]
    public void Token_With_Wrong_Secret_Should_Fail_Validation()
    {
        var jwt = CreateService();
        var token = jwt.GenerateToken(userId: 1, telegramId: 1, isAdmin: false);

        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();
        var wrongKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("AnotherSecretEntirelyDifferentFromTheOriginal!!"));

        var validation = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = TestIssuer,
            ValidAudience = TestAudience,
            IssuerSigningKey = wrongKey
        };

        var act = () => handler.ValidateToken(token, validation, out _);
        act.Should().Throw<SecurityTokenException>();
    }
}