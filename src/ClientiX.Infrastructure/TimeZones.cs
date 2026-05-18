namespace ClientiX.Infrastructure;

/// <summary>
/// Утилиты часовых поясов. Каждый мастер хранит свою IANA-зону в User.TimeZone.
/// </summary>
public static class TimeZones
{
    /// <summary>
    /// Получить TimeZoneInfo по строке IANA. Если не нашли — fallback на Москву.
    /// </summary>
    public static TimeZoneInfo Get(string? ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId)) ianaId = "Europe/Moscow";

        try { return TimeZoneInfo.FindSystemTimeZoneById(ianaId); }
        catch
        {
            // Windows-фоллбек: для распространённых российских поясов используем Win-имена.
            try { return TimeZoneInfo.FindSystemTimeZoneById(WindowsFallback(ianaId)); }
            catch
            {
                // Совсем грустный случай — отдаём UTC+3 как Москву.
                return TimeZoneInfo.CreateCustomTimeZone(
                    "MSK", TimeSpan.FromHours(3), "Moscow", "Moscow");
            }
        }
    }

    public static DateTime NowInZone(string? ianaId)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Get(ianaId));

    public static DateTime ToZone(DateTime utc, string? ianaId)
    {
        if (utc.Kind == DateTimeKind.Unspecified)
            utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc.ToUniversalTime(), Get(ianaId));
    }

    public static DateTime ZoneToUtc(DateTime local, string? ianaId)
    {
        var unspec = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspec, Get(ianaId));
    }

    /// <summary>
    /// Список основных российских часовых поясов для выбора в боте.
    /// Ключ — IANA, значение — отображаемая надпись для кнопки.
    /// </summary>
    public static readonly (string Id, string Label)[] RussianZones =
    {
        ("Europe/Kaliningrad",    "Калининград (UTC+2)"),
        ("Europe/Moscow",         "Москва, СПб (UTC+3)"),
        ("Europe/Samara",         "Самара, Ижевск (UTC+4)"),
        ("Asia/Yekaterinburg",    "Екатеринбург, Пермь (UTC+5)"),
        ("Asia/Omsk",             "Омск (UTC+6)"),
        ("Asia/Krasnoyarsk",      "Красноярск, Новосибирск (UTC+7)"),
        ("Asia/Irkutsk",          "Иркутск, Улан-Удэ (UTC+8)"),
        ("Asia/Yakutsk",          "Якутск, Чита (UTC+9)"),
        ("Asia/Vladivostok",      "Владивосток, Хабаровск (UTC+10)"),
        ("Asia/Magadan",          "Магадан (UTC+11)"),
        ("Asia/Kamchatka",        "Камчатка (UTC+12)"),
    };

    private static string WindowsFallback(string iana) => iana switch
    {
        "Europe/Moscow" => "Russian Standard Time",
        "Europe/Kaliningrad" => "Kaliningrad Standard Time",
        "Europe/Samara" => "Russia Time Zone 3",
        "Asia/Yekaterinburg" => "Ekaterinburg Standard Time",
        "Asia/Omsk" => "Omsk Standard Time",
        "Asia/Krasnoyarsk" => "North Asia Standard Time",
        "Asia/Irkutsk" => "North Asia East Standard Time",
        "Asia/Yakutsk" => "Yakutsk Standard Time",
        "Asia/Vladivostok" => "Vladivostok Standard Time",
        "Asia/Magadan" => "Magadan Standard Time",
        "Asia/Kamchatka" => "Russia Time Zone 11",
        _ => "UTC"
    };
}