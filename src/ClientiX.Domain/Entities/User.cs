namespace ClientiX.Domain.Entities;

/// <summary>
/// Пользователь платформы (мастер бьюти-индустрии).
/// </summary>
public class User
{
    public long Id { get; set; }
    public long TelegramId { get; set; }
    public string? TelegramUsername { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public string Role { get; set; } = "master"; // master | admin
    public long? ReferrerId { get; set; }
    public string ReferralCode { get; set; } = string.Empty;
    public bool HasMadeFirstPayment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Навигационные свойства
    public ManagedBot? ManagedBot { get; set; }
    public Subscription? Subscription { get; set; }
    public ICollection<Service> Services { get; set; } = new List<Service>();
    public ICollection<PortfolioItem> PortfolioItems { get; set; } = new List<PortfolioItem>();
}