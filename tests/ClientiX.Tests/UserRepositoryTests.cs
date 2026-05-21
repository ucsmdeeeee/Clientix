using ClientiX.Domain.Entities;
using ClientiX.Infrastructure.Persistence;
using ClientiX.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClientiX.Tests.Services;

/// <summary>
/// Юнит-тесты для <see cref="UserRepository"/>.
/// Используют EntityFrameworkCore InMemory-провайдер вместо реального PostgreSQL,
/// чтобы тесты были быстрыми и не требовали внешней инфраструктуры.
/// Покрывают создание мастеров, поиск по разным ключам и подсчёт статистики записей.
/// </summary>
public class UserRepositoryTests : IDisposable
{
    private readonly ClientiXDbContext _db;
    private readonly UserRepository _users;

    /// <summary>
    /// Конструктор создаёт изолированную InMemory-базу для каждого теста
    /// (имя БД с уникальным GUID), чтобы тесты не влияли друг на друга.
    /// </summary>
    public UserRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ClientiXDbContext>()
            .UseInMemoryDatabase($"clientix-test-{Guid.NewGuid()}")
            .Options;

        _db = new ClientiXDbContext(options);
        _users = new UserRepository(_db);
    }

    /// <summary>
    /// Освобождает ресурсы БД после каждого теста.
    /// </summary>
    public void Dispose()
    {
        _db.Dispose();
    }

    /// <summary>
    /// Проверяет, что метод CreateMasterAsync корректно создаёт нового мастера:
    /// присваивает Id, сохраняет переданные поля (TelegramId, Username, FirstName, LastName),
    /// устанавливает роль "master" и фиксирует время создания.
    /// </summary>
    [Fact]
    public async Task CreateMaster_Persists_User_With_Correct_Fields()
    {
        var user = await _users.CreateMasterAsync(
            telegramId: 777,
            username: "tester",
            firstName: "Test",
            lastName: "Master",
            ct: CancellationToken.None);

        user.Id.Should().BeGreaterThan(0);
        user.TelegramId.Should().Be(777);
        user.TelegramUsername.Should().Be("tester");
        user.FirstName.Should().Be("Test");
        user.LastName.Should().Be("Master");
        user.Role.Should().Be("master");
        user.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Проверяет поиск мастера по TelegramId.
    /// Это основной способ идентификации пользователя в боте (по id из update'а).
    /// </summary>
    [Fact]
    public async Task GetByTelegramId_Returns_Existing_User()
    {
        await _users.CreateMasterAsync(
            telegramId: 888, username: "u1", firstName: "Anna",
            lastName: null, ct: CancellationToken.None);

        var found = await _users.GetByTelegramIdAsync(888, CancellationToken.None);

        found.Should().NotBeNull();
        found!.TelegramId.Should().Be(888);
        found.FirstName.Should().Be("Anna");
    }

    /// <summary>
    /// Проверяет, что поиск несуществующего пользователя по TelegramId возвращает null,
    /// а не выбрасывает исключение. Это позволяет вызывающему коду создать нового мастера
    /// при первом /start от незарегистрированного TG-аккаунта.
    /// </summary>
    [Fact]
    public async Task GetByTelegramId_Returns_Null_For_Missing_User()
    {
        var found = await _users.GetByTelegramIdAsync(999999, CancellationToken.None);

        found.Should().BeNull();
    }

    /// <summary>
    /// Проверяет поиск мастера по внутреннему Id (primary key).
    /// Используется в JWT-авторизации: claim sub содержит внутренний UserId,
    /// и при каждом запросе мы получаем пользователя именно по этому Id.
    /// </summary>
    [Fact]
    public async Task GetById_Returns_Existing_User()
    {
        var created = await _users.CreateMasterAsync(
            telegramId: 555, username: "found_by_id", firstName: "Found",
            lastName: null, ct: CancellationToken.None);

        var byId = await _users.GetByIdAsync(created.Id, CancellationToken.None);

        byId.Should().NotBeNull();
        byId!.Id.Should().Be(created.Id);
        byId.TelegramUsername.Should().Be("found_by_id");
    }

    /// <summary>
    /// Главный тест статистики: создаём 4 записи с разными статусами
    /// (2 completed, 1 cancelled, 1 no_show) и проверяем, что GetStatsAsync
    /// правильно классифицирует их по статусам и суммирует выручку только по completed.
    /// Это критично для корректности данных, отображаемых на дашборде мастера.
    /// </summary>
    [Fact]
    public async Task GetStatsAsync_Counts_Bookings_By_Status_Correctly()
    {
        var master = await _users.CreateMasterAsync(
            telegramId: 100, username: "stats_master", firstName: "S",
            lastName: null, ct: CancellationToken.None);

        var now = DateTime.UtcNow;
        _db.Bookings.AddRange(
            new Booking
            {
                UserId = master.Id,
                ServiceId = 1,
                ClientTelegramId = 1,
                StartsAt = now.AddDays(-1),
                EndsAt = now.AddDays(-1).AddHours(1),
                DurationMinutes = 60,
                PriceRub = 1000,
                Status = "completed"
            },
            new Booking
            {
                UserId = master.Id,
                ServiceId = 1,
                ClientTelegramId = 2,
                StartsAt = now.AddDays(-2),
                EndsAt = now.AddDays(-2).AddHours(1),
                DurationMinutes = 60,
                PriceRub = 1500,
                Status = "completed"
            },
            new Booking
            {
                UserId = master.Id,
                ServiceId = 1,
                ClientTelegramId = 3,
                StartsAt = now.AddDays(-3),
                EndsAt = now.AddDays(-3).AddHours(1),
                DurationMinutes = 60,
                PriceRub = 500,
                Status = "cancelled_by_client"
            },
            new Booking
            {
                UserId = master.Id,
                ServiceId = 1,
                ClientTelegramId = 4,
                StartsAt = now.AddDays(-4),
                EndsAt = now.AddDays(-4).AddHours(1),
                DurationMinutes = 60,
                PriceRub = 800,
                Status = "no_show"
            }
        );
        await _db.SaveChangesAsync();

        var stats = await _users.GetStatsAsync(
            master.Id, now.AddDays(-10), now.AddDays(1), CancellationToken.None);

        stats.Total.Should().Be(4);
        stats.Completed.Should().Be(2);
        stats.CancelledByClient.Should().Be(1);
        stats.NoShow.Should().Be(1);
        stats.RevenueRub.Should().Be(2500); // 1000 + 1500 от двух completed
    }

    /// <summary>
    /// Проверяет, что GetStatsAsync строго соблюдает временной диапазон [fromUtc; toUtc).
    /// Запись, выпадающая за диапазон, не должна попадать в подсчёт ни по количеству,
    /// ни по сумме выручки. Это гарантирует, что метрики "за 7 дней" не подтянут
    /// старые записи и не введут мастера в заблуждение.
    /// </summary>
    [Fact]
    public async Task GetStatsAsync_Excludes_Bookings_Outside_Range()
    {
        var master = await _users.CreateMasterAsync(
            telegramId: 200, username: "range_test", firstName: "R",
            lastName: null, ct: CancellationToken.None);

        var now = DateTime.UtcNow;
        _db.Bookings.AddRange(
            // Внутри диапазона [-10; +1] дней — должна посчитаться
            new Booking
            {
                UserId = master.Id,
                ServiceId = 1,
                ClientTelegramId = 1,
                StartsAt = now.AddDays(-1),
                EndsAt = now.AddDays(-1).AddHours(1),
                PriceRub = 1000,
                Status = "completed"
            },
            // Снаружи диапазона (-100 дней) — должна быть проигнорирована
            new Booking
            {
                UserId = master.Id,
                ServiceId = 1,
                ClientTelegramId = 2,
                StartsAt = now.AddDays(-100),
                EndsAt = now.AddDays(-100).AddHours(1),
                PriceRub = 5000,
                Status = "completed"
            }
        );
        await _db.SaveChangesAsync();

        var stats = await _users.GetStatsAsync(
            master.Id, now.AddDays(-10), now.AddDays(1), CancellationToken.None);

        stats.Total.Should().Be(1);
        stats.RevenueRub.Should().Be(1000); // не учитываем выручку от записи 100-дневной давности
    }
}