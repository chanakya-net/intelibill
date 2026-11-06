using Intelibill.Domain.Common;

namespace Intelibill.Integration.Tests;

/// <summary>
/// The shop's business date as the product computes it (Asia/Kolkata, UTC+05:30, no DST).
/// Tests that build a "today" or a date window must use this, never the UTC date: between
/// 18:30 and 24:00 UTC the two differ.
/// </summary>
internal static class BusinessDates
{
    public static DateOnly Of(DateTimeOffset instant) =>
        BusinessCalendar.DateOf(instant);

    public static DateOnly Today => Of(DateTimeOffset.UtcNow);
}
