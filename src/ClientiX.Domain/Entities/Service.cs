namespace ClientiX.Domain.Entities;

/// <summary>
/// Услуга, оказываемая мастером.
/// </summary>
public class Service
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public int PriceRub { get; set; }
    public int BufferAfterMinutes { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}