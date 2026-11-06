using Intelibill.Application.Common.Time;
using Intelibill.Domain.Common;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Infrastructure.Services.Time;

public sealed class BusinessClock(TimeProvider timeProvider) : IBusinessClock
{
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    public DateOnly GetBusinessDate(DateTimeOffset instant, string timeZoneId) =>
        BusinessCalendar.DateOf(instant, BusinessTimeZones.Resolve(timeZoneId));

    public BusinessDay GetBusinessDay(DateTimeOffset instant, string timeZoneId) =>
        new(GetBusinessDate(instant, timeZoneId), timeZoneId);

    public DateOnly Today(string timeZoneId) => GetBusinessDate(UtcNow, timeZoneId);

    public UtcDateWindow GetUtcWindow(DateOnly fromInclusive, DateOnly toInclusive, string timeZoneId)
    {
        var zone = BusinessTimeZones.Resolve(timeZoneId);
        return new UtcDateWindow(
            BusinessCalendar.StartOf(fromInclusive, zone),
            BusinessCalendar.StartOf(toInclusive.AddDays(1), zone));
    }
}
