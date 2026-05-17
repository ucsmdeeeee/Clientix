namespace ClientiX.Domain.Entities;

/// <summary>
/// Платёж за подписку через ЮKassa.
/// </summary>
public class Payment
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public int TariffPlanId { get; set; }
    public string? YkPaymentId { get; set; }
    public int AmountRub { get; set; }
    public string Status { get; set; } = "pending"; // pending | succeeded | canceled | refunded
    public bool IsFirstPayment { get; set; }
    public string? RawResponse { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }

    public User User { get; set; } = null!;
    public TariffPlan TariffPlan { get; set; } = null!;
}