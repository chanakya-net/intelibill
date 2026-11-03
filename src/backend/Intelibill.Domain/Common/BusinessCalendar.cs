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
}
