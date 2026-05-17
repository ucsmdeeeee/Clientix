namespace ClientiX.Domain.Entities;

/// <summary>
/// Подписка мастера на платформу.
/// </summary>
public class Subscription
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Status { get; set; } = "trial"; // trial | active | expired | cancelled
    public DateTime CurrentPeriodEnd { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public int? LastTariffPlanId { get; set; }
    public bool AutoRenew { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public TariffPlan? LastTariffPlan { get; set; }
}