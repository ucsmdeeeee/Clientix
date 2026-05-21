using ClientiX.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace ClientiX.Tests.Services;

/// <summary>
/// Юнит-тесты для доменной сущности <see cref="Booking"/>.
/// Проверяют значения по умолчанию: новая запись должна сразу иметь корректный статус
/// и автоматически проставленное время создания (это важно для бизнес-логики
/// — все записи начинают жизнь в статусе "pending" до подтверждения).
/// </summary>
public class BookingEntityTests
{
    /// <summary>
    /// Любая новая запись клиента должна по умолчанию быть в статусе "pending"
    /// (ожидает подтверждения мастером, ещё не оказана услуга).
    /// </summary>
    [Fact]
    public void Booking_Has_Default_Pending_Status()
    {
        var booking = new Booking();

        booking.Status.Should().Be("pending");
    }

    /// <summary>
    /// Время создания записи (CreatedAt) должно автоматически проставляться
    /// в текущий UTC момент при создании объекта в памяти.
    /// Это упрощает код в репозитории — не нужно явно выставлять CreatedAt.
    /// </summary>
    [Fact]
    public void Booking_CreatedAt_Is_Set_To_Current_Utc_By_Default()
    {
        var before = DateTime.UtcNow;

        var booking = new Booking();

        booking.CreatedAt.Should().BeOnOrAfter(before);
        booking.CreatedAt.Should().BeOnOrBefore(DateTime.UtcNow.AddSeconds(1));
    }
}

/// <summary>
/// Юнит-тесты для доменной сущности <see cref="User"/>.
/// Проверяют значения по умолчанию для нового пользователя (мастера):
/// роль, часовой пояс, горизонт записи и настройки напоминаний.
/// Эти defaults обеспечивают разумное стартовое поведение для каждого
/// нового зарегистрированного мастера без необходимости явной настройки.
/// </summary>
public class UserEntityTests
{
    /// <summary>
    /// Любой новый зарегистрированный пользователь — это мастер бьюти-индустрии.
    /// Роль "admin" присваивается отдельно (вручную в БД для технического админа платформы).
    /// </summary>
    [Fact]
    public void User_Has_Default_Master_Role()
    {
        var user = new User();

        user.Role.Should().Be("master");
    }

    /// <summary>
    /// По умолчанию таймзона — Europe/Moscow (большинство наших пользователей в РФ).
    /// Мастер может изменить её в настройках, но для нового аккаунта нужна разумная стартовая.
    /// </summary>
    [Fact]
    public void User_Has_Default_Moscow_Timezone()
    {
        var user = new User();

        user.TimeZone.Should().Be("Europe/Moscow");
    }

    /// <summary>
    /// Клиенты по умолчанию могут записаться на 14 дней вперёд.
    /// Это разумный баланс между удобством клиентов и контролем расписания мастера.
    /// </summary>
    [Fact]
    public void User_Has_Default_14_Day_Booking_Horizon()
    {
        var user = new User();

        user.BookingHorizonDays.Should().Be(14);
    }

    /// <summary>
    /// Напоминание клиенту за 24 часа до записи включено по умолчанию —
    /// это снижает количество no_show'ов и важно для качества сервиса мастера.
    /// </summary>
    [Fact]
    public void User_Has_Reminder_Day_Before_Enabled_By_Default()
    {
        var user = new User();

        user.ReminderDayBefore.Should().BeTrue();
    }
}