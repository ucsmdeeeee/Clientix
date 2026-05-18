using ClientiX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClientiX.Infrastructure.Persistence;

/// <summary>
/// Контекст базы данных платформы ClientiX.
/// Использует PostgreSQL через Npgsql provider.
/// </summary>
public class ClientiXDbContext : DbContext
{
    public ClientiXDbContext(DbContextOptions<ClientiXDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<ManagedBot> ManagedBots => Set<ManagedBot>();
    public DbSet<TariffPlan> TariffPlans => Set<TariffPlan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<PortfolioItem> PortfolioItems => Set<PortfolioItem>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<WorkScheduleTemplate> WorkScheduleTemplates => Set<WorkScheduleTemplate>();
    public DbSet<WorkScheduleException> WorkScheduleExceptions => Set<WorkScheduleException>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // --- User ---
        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.HasIndex(u => u.TelegramId).IsUnique();
            b.HasIndex(u => u.ReferralCode).IsUnique();
            b.Property(u => u.Role).HasMaxLength(16);
            b.Property(u => u.TelegramUsername).HasMaxLength(64);
            b.Property(u => u.FirstName).HasMaxLength(128);
            b.Property(u => u.LastName).HasMaxLength(128);
            b.Property(u => u.Phone).HasMaxLength(32);
            b.Property(u => u.ReferralCode).HasMaxLength(16).IsRequired();
            b.Property(x => x.TimeZone)
                .HasMaxLength(64)
                .HasDefaultValue("Europe/Moscow")
                .IsRequired();
            b.Property(x => x.BookingHorizonDays).HasDefaultValue(14).IsRequired();
        });

        // --- ManagedBot ---
        modelBuilder.Entity<ManagedBot>(b =>
        {
            b.ToTable("managed_bots");
            b.HasIndex(x => x.BotTelegramId).IsUnique();
            b.HasIndex(x => x.UserId).IsUnique();
            b.Property(x => x.BotUsername).HasMaxLength(64).IsRequired();
            b.Property(x => x.BotTokenEncrypted).IsRequired();
            b.Property(x => x.WebhookSecret).HasMaxLength(64).IsRequired();
            b.Property(x => x.Niche).HasMaxLength(32);
            b.Property(x => x.DisplayName).HasMaxLength(128);
            b.Property(x => x.City).HasMaxLength(128);
            b.HasOne(x => x.User).WithOne(u => u.ManagedBot)
             .HasForeignKey<ManagedBot>(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // --- TariffPlan ---
        modelBuilder.Entity<TariffPlan>(b =>
        {
            b.ToTable("tariff_plans");
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.Code).HasMaxLength(32).IsRequired();

            // Сидинг тарифов
            b.HasData(
                new TariffPlan { Id = 1, Code = "days_30", DurationDays = 30, PriceFirstRub = 300, PriceRenewRub = 500, IsActive = true, SortOrder = 1 },
                new TariffPlan { Id = 2, Code = "days_90", DurationDays = 90, PriceFirstRub = 1000, PriceRenewRub = 1300, IsActive = true, SortOrder = 2 },
                new TariffPlan { Id = 3, Code = "days_180", DurationDays = 180, PriceFirstRub = 2000, PriceRenewRub = 2400, IsActive = true, SortOrder = 3 }
            );
        });

        // --- Subscription ---
        modelBuilder.Entity<Subscription>(b =>
        {
            b.ToTable("subscriptions");
            b.HasIndex(x => x.UserId).IsUnique();
            b.HasIndex(x => x.Status);
            b.HasIndex(x => x.CurrentPeriodEnd);
            b.Property(x => x.Status).HasMaxLength(16).IsRequired();
            b.HasOne(x => x.User).WithOne(u => u.Subscription)
             .HasForeignKey<Subscription>(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.LastTariffPlan).WithMany()
             .HasForeignKey(x => x.LastTariffPlanId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // --- Payment ---
        modelBuilder.Entity<Payment>(b =>
        {
            b.ToTable("payments");
            b.HasIndex(x => x.YkPaymentId).IsUnique();
            b.HasIndex(x => x.UserId);
            b.HasIndex(x => x.Status);
            b.Property(x => x.Status).HasMaxLength(16).IsRequired();
            b.Property(x => x.YkPaymentId).HasMaxLength(64);
            b.HasOne(x => x.User).WithMany()
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.TariffPlan).WithMany()
             .HasForeignKey(x => x.TariffPlanId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // --- Service ---
        modelBuilder.Entity<Service>(b =>
        {
            b.ToTable("services");
            b.HasIndex(x => new { x.UserId, x.IsActive });
            b.Property(x => x.Name).HasMaxLength(128).IsRequired();
            b.HasOne(x => x.User).WithMany(u => u.Services)
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // --- PortfolioItem ---
        modelBuilder.Entity<PortfolioItem>(b =>
        {
            b.ToTable("portfolio_items");
            b.HasIndex(x => new { x.UserId, x.SortOrder });
            b.Property(x => x.TelegramFileId).HasMaxLength(256).IsRequired();
            b.HasOne(x => x.User).WithMany(u => u.PortfolioItems)
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // --- Booking ---
        modelBuilder.Entity<Booking>(b =>
        {
            b.ToTable("bookings");

            b.Property(x => x.Status).HasMaxLength(32).IsRequired();
            b.Property(x => x.CancelledBy).HasMaxLength(16);
            b.Property(x => x.CancellationReason).HasMaxLength(256);
            b.Property(x => x.ClientFirstName).HasMaxLength(64);
            b.Property(x => x.ClientUsername).HasMaxLength(64);

            // Главный индекс для запросов «записи мастера на дату»
            b.HasIndex(x => new { x.UserId, x.StartsAt });

            // Индекс для запросов «записи клиента»
            b.HasIndex(x => new { x.ClientTelegramId, x.StartsAt });

            // Уникальный частичный индекс — защита от двойной записи на одно время.
            // Активные статусы (pending, confirmed) не могут пересекаться по starts_at.
            b.HasIndex(x => new { x.UserId, x.StartsAt })
             .HasFilter("status IN ('pending', 'confirmed')")
             .HasDatabaseName("idx_bookings_no_overlap")
             .IsUnique();

            b.HasOne(x => x.User).WithMany()
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.Service).WithMany()
             .HasForeignKey(x => x.ServiceId)
             .OnDelete(DeleteBehavior.Restrict);

            b.Property(x => x.AdditionalServiceIds).HasMaxLength(256);
        });

        // --- WorkScheduleTemplate ---
        modelBuilder.Entity<WorkScheduleTemplate>(b =>
        {
            b.ToTable("work_schedule_templates");
            b.HasIndex(x => new { x.UserId, x.DayOfWeek }).IsUnique();
            b.HasOne(x => x.User).WithMany(u => u.ScheduleTemplates)
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // --- WorkScheduleException ---
        modelBuilder.Entity<WorkScheduleException>(b =>
        {
            b.ToTable("work_schedule_exceptions");
            b.HasIndex(x => new { x.UserId, x.Date }).IsUnique();
            b.Property(x => x.Note).HasMaxLength(256);
            b.HasOne(x => x.User).WithMany(u => u.ScheduleExceptions)
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // --- PortfolioItem ---
        modelBuilder.Entity<PortfolioItem>(b =>
        {
            b.ToTable("portfolio_items");
            b.HasIndex(x => new { x.UserId, x.SortOrder });
            b.Property(x => x.TelegramFileId).HasMaxLength(256).IsRequired();
            b.Property(x => x.FileIdsPerBot)
                .HasColumnType("jsonb")
                .HasConversion(
            v => v == null ? null : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
            v => v == null ? null : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(v, (System.Text.Json.JsonSerializerOptions?)null));
            b.Property(x => x.Caption).HasMaxLength(512);
            b.HasOne(x => x.User).WithMany(u => u.PortfolioItems)
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}