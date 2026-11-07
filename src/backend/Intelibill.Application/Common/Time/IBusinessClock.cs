using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Common.Time;

/// <summary>Reads instants as the business date a shop sees in its own time zone.</summary>
public interface IBusinessClock
{
    DateTimeOffset UtcNow { get; }

    DateOnly GetBusinessDate(DateTimeOffset instant, string timeZoneId);

    BusinessDay GetBusinessDay(DateTimeOffset instant, string timeZoneId);

    DateOnly Today(string timeZoneId);

    /// <summary>The instants covering <paramref name="fromInclusive"/> through <paramref name="toInclusive"/>; the end is the start of the day after.</summary>
    UtcDateWindow GetUtcWindow(DateOnly fromInclusive, DateOnly toInclusive, string timeZoneId);
}
