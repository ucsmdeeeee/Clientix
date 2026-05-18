using ClientiX.Domain.Entities;
using ClientiX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClientiX.Infrastructure.Repositories;

/// <summary>
/// Репозиторий работы с платежами и подписками.
/// </summary>
public class PaymentRepository
{
    private readonly ClientiXDbContext _db;

    public PaymentRepository(ClientiXDbContext db)
    {
        _db = db;
    }

    public async Task<TariffPlan?> GetTariffByCodeAsync(string code, CancellationToken ct)
    {
        return await _db.TariffPlans
            .FirstOrDefaultAsync(t => t.Code == code && t.IsActive, ct);
    }

    /// <summary>
    /// Создаёт запись платежа в статусе pending.
    /// </summary>
    public async Task<Payment> CreatePendingPaymentAsync(
        long userId, int tariffPlanId, int amountRub, bool isFirstPayment, CancellationToken ct)
    {
        var payment = new Payment
        {
            UserId = userId,
            TariffPlanId = tariffPlanId,
            AmountRub = amountRub,
            Status = "pending",
            IsFirstPayment = isFirstPayment,
            CreatedAt = DateTime.UtcNow
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct);
        return payment;
    }

    public async Task<Payment?> GetByIdAsync(long paymentId, CancellationToken ct)
    {
        return await _db.Payments
            .Include(p => p.User).ThenInclude(u => u.Subscription)
            .Include(p => p.TariffPlan)
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct);
    }

    /// <summary>
    /// Помечает платёж как успешный и продлевает подписку.
    /// </summary>
    public async Task<bool> MarkAsSucceededAndExtendSubscriptionAsync(
        long paymentId, string? ykPaymentId, CancellationToken ct)
    {
        var payment = await _db.Payments
            .Include(p => p.User).ThenInclude(u => u.Subscription)
            .Include(p => p.TariffPlan)
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct);

        if (payment is null) return false;
        if (payment.Status == "succeeded") return true; // идемпотентность

        payment.Status = "succeeded";
        payment.PaidAt = DateTime.UtcNow;
        payment.YkPaymentId = ykPaymentId;

        var user = payment.User;
        var sub = user.Subscription ?? new Subscription
        {
            UserId = user.Id,
            CurrentPeriodEnd = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        // Если подписка истекла или это первое продление — отсчитываем от сейчас.
        // Если ещё активна (или триал) — добавляем к текущей дате окончания.
        var startFrom = sub.CurrentPeriodEnd > DateTime.UtcNow
            ? sub.CurrentPeriodEnd
            : DateTime.UtcNow;

        sub.CurrentPeriodEnd = startFrom.AddDays(payment.TariffPlan.DurationDays);
        sub.Status = "active";
        sub.LastTariffPlanId = payment.TariffPlanId;
        sub.UpdatedAt = DateTime.UtcNow;

        if (user.Subscription is null) _db.Subscriptions.Add(sub);
        user.HasMadeFirstPayment = true;
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return true;
    }
}