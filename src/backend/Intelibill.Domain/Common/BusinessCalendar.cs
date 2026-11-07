namespace Intelibill.Domain.Common;

/// <summary>
/// The calendar business dates and fiscal years are read in: India Standard Time, a fixed
/// UTC+05:30 with no daylight saving. Until shops carry their own time zone every shop uses it.
/// </summary>
public static class BusinessCalendar
{
    public static readonly TimeSpan Offset = TimeSpan.FromMinutes(330);

    public static DateOnly DateOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(Offset).DateTime);

    /// <summary>The instant the given business date begins, in UTC (the only offset timestamptz stores).</summary>
    public static DateTimeOffset StartOf(DateOnly date) =>
        new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, Offset).ToUniversalTime();

    /// <summary>The business date an instant falls on in the given zone.</summary>
    public static DateOnly DateOf(DateTimeOffset instant, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
    }

    /// <summary>The instant the given business date begins in the given zone, in UTC.</summary>
    public static DateTimeOffset StartOf(DateOnly date, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
            local = local.AddHours(1);

        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
