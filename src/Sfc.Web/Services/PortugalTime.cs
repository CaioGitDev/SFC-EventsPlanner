namespace Sfc.Web.Services;

/// <summary>
/// Single-country app: user-facing times are Europe/Lisbon regardless of the
/// server OS time zone (containers commonly run UTC — ToLocalTime would drift).
/// </summary>
public static class PortugalTime
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");

    public static DateOnly Today => DateOnly.FromDateTime(FromUtc(DateTime.UtcNow));

    public static DateTime FromUtc(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);
}
