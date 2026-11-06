using Intelibill.Domain.Common;

namespace Intelibill.Domain.ValueObjects;

/// <summary>A calendar date as a shop reads it, with the zone it was read in.</summary>
public readonly record struct BusinessDay(DateOnly Date, string TimeZoneId)
{
    /// <summary>The day an instant falls on in the default zone, for callers that have no shop zone to hand.</summary>
    public static BusinessDay InDefaultZone(DateTimeOffset instant) =>
        new(BusinessCalendar.DateOf(instant), BusinessTimeZones.DefaultId);
}
