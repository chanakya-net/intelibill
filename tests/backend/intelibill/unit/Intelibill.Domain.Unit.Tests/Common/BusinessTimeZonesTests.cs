using Intelibill.Domain.Common;

namespace Intelibill.Domain.Unit.Tests.Common;

public class BusinessTimeZonesTests
{
    [Fact]
    public void Lookup_WhenTheDefaultZoneIsMissingFromTheOs_FallsBackToFixedIndiaOffset()
    {
        var zone = BusinessTimeZones.Lookup(BusinessTimeZones.DefaultId, _ => throw new TimeZoneNotFoundException());

        Assert.Equal(TimeSpan.FromMinutes(330), zone.BaseUtcOffset);
        Assert.False(zone.SupportsDaylightSavingTime);
        Assert.Equal(new DateOnly(2026, 10, 21), BusinessCalendar.DateOf(DateTimeOffset.Parse("2026-10-20T20:30:00Z", System.Globalization.CultureInfo.InvariantCulture), zone));
    }

    [Fact]
    public void Lookup_WhenTheDefaultZoneIsInvalidOnTheOs_FallsBackToFixedIndiaOffset()
    {
        var zone = BusinessTimeZones.Lookup(BusinessTimeZones.DefaultId, _ => throw new InvalidTimeZoneException());

        Assert.Equal(TimeSpan.FromMinutes(330), zone.BaseUtcOffset);
    }

    [Fact]
    public void Lookup_ForAnotherUnknownZone_StillThrows() =>
        Assert.Throws<TimeZoneNotFoundException>(() =>
            BusinessTimeZones.Lookup("Mars/Olympus", _ => throw new TimeZoneNotFoundException()));

    [Fact]
    public void Resolve_ReturnsTheSystemZoneWhenItExists() =>
        Assert.Equal(TimeSpan.FromMinutes(330), BusinessTimeZones.Resolve(BusinessTimeZones.DefaultId).BaseUtcOffset);
}
