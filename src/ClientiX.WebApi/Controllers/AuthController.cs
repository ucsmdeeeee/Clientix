using ClientiX.Infrastructure.Repositories;
using ClientiX.WebApi.DTOs;
using ClientiX.WebApi.Services;
using Microsoft.AspNetCore.Mvc;

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
}