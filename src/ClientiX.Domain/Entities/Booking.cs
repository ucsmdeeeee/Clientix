namespace ClientiX.Domain.Entities;

/// <summary>
/// Запись клиента к мастеру. Статусы: pending → confirmed → completed | cancelled
/// </summary>
public class Booking
{
    public long Id { get; set; }

    /// <summary>ID мастера в нашей системе.</summary>
    public long UserId { get; set; }

    /// <summary>ID услуги в каталоге мастера.</summary>
    public long ServiceId { get; set; }

    /// <summary>Telegram ID клиента, который записался.</summary>
    public long ClientTelegramId { get; set; }

    /// <summary>Имя клиента (FirstName из Telegram).</summary>
    public string? ClientFirstName { get; set; }

    /// <summary>Username клиента (без @).</summary>
    public string? ClientUsername { get; set; }

    /// <summary>Дата и время начала, UTC.</summary>
    public DateTime StartsAt { get; set; }

    /// <summary>Дата и время окончания, UTC.</summary>
    public DateTime EndsAt { get; set; }

    /// <summary>Длительность из услуги в момент записи.</summary>
    public int DurationMinutes { get; set; }

    /// <summary>Цена услуги в момент записи.</summary>
    public int PriceRub { get; set; }

    /// <summary>
    /// Текущий статус. Возможные:
    /// pending — клиент записался, мастер ещё не подтвердил/не пришёл
    /// confirmed — мастер подтвердил (или авто-подтверждение)
    /// completed — услуга оказана
    /// cancelled_by_client — клиент отменил
    /// cancelled_by_master — мастер отменил
    /// no_show — клиент не пришёл
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>Кто отменил (если статус cancelled): «client» или «master».</summary>
    public string? CancelledBy { get; set; }

    /// <summary>Причина отмены (необязательно).</summary>
    public string? CancellationReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
    public Service Service { get; set; } = null!;
}