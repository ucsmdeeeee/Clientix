namespace ClientiX.Domain.Entities;

/// <summary>
/// Тарифный план подписки на платформу ClientiX.
/// </summary>
public class TariffPlan
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty; // days_30, days_90, days_180
    public int DurationDays { get; set; }
    public int PriceFirstRub { get; set; }    // цена первой оплаты
    public int PriceRenewRub { get; set; }    // цена продления
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}