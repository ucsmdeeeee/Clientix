using ClientiX.Infrastructure.Repositories;
using ClientiX.WebApi.DTOs;
using ClientiX.WebApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace ClientiX.WebApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly TelegramAuthService _telegramAuth;
    private readonly JwtService _jwt;
    private readonly UserRepository _users;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        TelegramAuthService telegramAuth,
        JwtService jwt,
        UserRepository users,
        IConfiguration config,
        ILogger<AuthController> logger)
    {
        _telegramAuth = telegramAuth;
        _jwt = jwt;
        _users = users;
        _config = config;
        _logger = logger;
    }

    [HttpPost("telegram")]
    public async Task<IActionResult> LoginViaTelegram(
        [FromBody] TelegramLoginDto data, CancellationToken ct)
    {
        if (data is null || data.Id == 0 || string.IsNullOrEmpty(data.Hash))
            return BadRequest(new { error = "invalid_data" });

        if (!_telegramAuth.VerifyLoginData(data))
            return Unauthorized(new { error = "invalid_signature" });

        // Ищем мастера в БД, или создаём
        var user = await _users.GetByTelegramIdAsync(data.Id, ct);
        if (user is null)
        {
            user = await _users.CreateMasterAsync(
                telegramId: data.Id,
                username: data.Username,
                firstName: data.First_name,
                lastName: data.Last_name,
                ct: ct);

            _logger.LogInformation("Веб-регистрация: новый мастер id={UserId}, tg={TgId}",
                user.Id, user.TelegramId);
        }

        var adminId = _config.GetValue<long>("Telegram:AdminTelegramId");
        var isAdmin = data.Id == adminId;

        var token = _jwt.GenerateToken(user.Id, user.TelegramId, isAdmin);

        return Ok(new AuthResponseDto
        {
            Token = token,
            UserId = user.Id,
            TelegramId = user.TelegramId,
            FirstName = user.FirstName,
            Username = user.TelegramUsername,
            IsAdmin = isAdmin
        });
    }

    [HttpPost("generate-web-token")]
    public async Task<IActionResult> GenerateWebToken(
        [FromHeader(Name = "X-Internal-Secret")] string? secret,
        [FromBody] GenerateWebTokenDto dto,
        CancellationToken ct)
    {
        var expectedSecret = _config["Internal:Secret"];
        if (string.IsNullOrEmpty(expectedSecret) || secret != expectedSecret)
            return Unauthorized();

        if (dto.TelegramId == 0)
            return BadRequest(new { error = "invalid_telegram_id" });

        var user = await _users.GetByTelegramIdAsync(dto.TelegramId, ct);
        if (user is null)
            return NotFound(new { error = "user_not_found" });

        var adminId = _config.GetValue<long>("Telegram:AdminTelegramId");
        var isAdmin = dto.TelegramId == adminId;

        // Короткий токен — 5 минут
        var token = _jwt.GenerateShortLivedToken(user.Id, user.TelegramId, isAdmin);

        return Ok(new
        {
            token,
            userId = user.Id,
            telegramId = user.TelegramId,
            firstName = user.FirstName,
            username = user.TelegramUsername,
            isAdmin
        });
    }

    [HttpPost("exchange")]
    public async Task<IActionResult> ExchangeToken(
        [FromBody] ExchangeTokenDto dto,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(dto.ShortToken))
            return BadRequest(new { error = "missing_token" });

        // Отключаем automatic claim mapping для этого handler
        // иначе "sub" будет перемаплен в ClaimTypes.NameIdentifier и FindFirst("sub") вернёт null
        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!));

        try
        {
            var principal = handler.ValidateToken(dto.ShortToken, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _config["Jwt:Issuer"],
                ValidAudience = _config["Jwt:Audience"],
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.FromSeconds(30)
            }, out _);

            var tokenType = principal.FindFirst("type")?.Value;
            if (tokenType != "web_login")
            {
                _logger.LogWarning("Token exchange: wrong type='{Type}'", tokenType ?? "null");
                return Unauthorized(new { error = "wrong_token_type" });
            }

            var subClaim = principal.FindFirst("sub")?.Value;
            var tgClaim = principal.FindFirst("tg_id")?.Value;

            if (string.IsNullOrEmpty(subClaim) || string.IsNullOrEmpty(tgClaim))
            {
                _logger.LogWarning(
                    "Token exchange: missing claims. sub='{Sub}' tg_id='{Tg}'. AllClaims: {Claims}",
                    subClaim ?? "null",
                    tgClaim ?? "null",
                    string.Join(",", principal.Claims.Select(c => $"{c.Type}={c.Value}")));
                return Unauthorized(new { error = "invalid_token" });
            }

            if (!long.TryParse(subClaim, out var userId))
                return Unauthorized(new { error = "invalid_token" });
            if (!long.TryParse(tgClaim, out var telegramId))
                return Unauthorized(new { error = "invalid_token" });

            var isAdmin = principal.FindFirst("is_admin")?.Value == "true";

            var user = await _users.GetByIdAsync(userId, ct);
            if (user is null)
            {
                _logger.LogWarning("Token exchange: user {UserId} not found in DB", userId);
                return NotFound();
            }

            var longToken = _jwt.GenerateToken(userId, telegramId, isAdmin);

            _logger.LogInformation("Token exchanged for userId={UserId} (tg={TgId})", userId, telegramId);

            return Ok(new AuthResponseDto
            {
                Token = longToken,
                UserId = userId,
                TelegramId = telegramId,
                FirstName = user.FirstName,
                Username = user.TelegramUsername,
                IsAdmin = isAdmin
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token exchange failed");
            return Unauthorized(new { error = "invalid_token" });
        }
    }
}