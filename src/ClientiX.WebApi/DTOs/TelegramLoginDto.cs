namespace ClientiX.WebApi.DTOs;

public record TelegramLoginDto
{
    public long Id { get; init; }
    public string? First_name { get; init; }
    public string? Last_name { get; init; }
    public string? Username { get; init; }
    public string? Photo_url { get; init; }
    public long Auth_date { get; init; }
    public string Hash { get; init; } = null!;
}

public record AuthResponseDto
{
    public string Token { get; init; } = null!;
    public long UserId { get; init; }
    public long TelegramId { get; init; }
    public string? FirstName { get; init; }
    public string? Username { get; init; }
    public bool IsAdmin { get; init; }
}

public record GenerateWebTokenDto
{
    public long TelegramId { get; init; }
}

public record ExchangeTokenDto
{
    public string ShortToken { get; init; } = null!;
}