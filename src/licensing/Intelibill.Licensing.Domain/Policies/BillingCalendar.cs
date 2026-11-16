namespace Intelibill.Licensing.Domain.Policies;

public enum BillingPeriod { Month, Year }

/// <summary>
/// The retained calendar anchor of a subscription: the original local start in the billing zone and its period.
/// Every boundary is computed from this anchor and an ordinal, never by chaining clipped dates.
/// </summary>
public sealed record BillingAnchor(DateTime LocalStart, string TimeZone, BillingPeriod Period);

/// <summary>
/// Versioned calendar-month/year arithmetic in the declared billing zone. 31 Jan 10:00 IST runs to 28 Feb then
/// 31 Mar; 29 Feb annual runs to 28 Feb in common years and back to 29 Feb in leap years.
/// </summary>
public static class BillingCalendar
{
    public const string Version = "calendar-month-year/1";
    public const string Zone = "Asia/Kolkata";

    private static readonly TimeZoneInfo Kolkata = TimeZoneInfo.FindSystemTimeZoneById(Zone);

    /// <summary>True when the anchor names the supported zone and a whole-second local start.</summary>
    public static bool IsSupported(BillingAnchor? anchor) =>
        anchor is { TimeZone: Zone } && anchor.LocalStart.Kind != DateTimeKind.Utc
        && anchor.LocalStart.Ticks % TimeSpan.TicksPerSecond == 0 && Enum.IsDefined(anchor.Period);

    /// <summary>The UTC instant of boundary <paramref name="ordinal"/> (0 is the anchor itself).</summary>
    public static DateTimeOffset Boundary(BillingAnchor anchor, int ordinal)
    {
        if (!IsSupported(anchor))
            throw new ArgumentException("The billing anchor uses an unsupported zone or start.", nameof(anchor));
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);

        var start = anchor.LocalStart;
        var months = anchor.Period == BillingPeriod.Month ? ordinal : checked(ordinal * 12);
        var firstOfMonth = new DateTime(start.Year, start.Month, 1).AddMonths(months);
        var day = Math.Min(start.Day, DateTime.DaysInMonth(firstOfMonth.Year, firstOfMonth.Month));
        var local = DateTime.SpecifyKind(firstOfMonth.AddDays(day - 1) + start.TimeOfDay, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Kolkata), TimeSpan.Zero);
    }

    /// <summary>The <c>[start, end)</c> interval of period <paramref name="ordinal"/>.</summary>
    public static (DateTimeOffset StartsAt, DateTimeOffset EndsAt) Period(BillingAnchor anchor, int ordinal) =>
        (Boundary(anchor, ordinal), Boundary(anchor, checked(ordinal + 1)));

    /// <summary>The local wall-clock start of an instant in the billing zone, truncated to whole seconds.</summary>
    public static DateTime LocalStart(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, Kolkata).DateTime;
        return DateTime.SpecifyKind(new DateTime(local.Ticks - local.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Unspecified);
    }
}
