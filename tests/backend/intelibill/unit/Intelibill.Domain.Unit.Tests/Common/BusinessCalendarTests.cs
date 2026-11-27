using System.Globalization;
using Intelibill.Domain.Common;

namespace Intelibill.Domain.Unit.Tests.Common;

public class BusinessCalendarTests
{
    private static readonly TimeZoneInfo Kolkata = BusinessTimeZones.Resolve(BusinessTimeZones.DefaultId);

    private static DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    [Fact]
    public void DateOf_ReadsTheInstantInTheShopZone() =>
        Assert.Equal(new DateOnly(2026, 10, 21), BusinessCalendar.DateOf(Utc("2026-10-20T20:30:00Z"), Kolkata));

    [Fact]
    public void DateOf_TurnsOverAtIstMidnight()
    {
        Assert.Equal(new DateOnly(2026, 10, 20), BusinessCalendar.DateOf(Utc("2026-10-20T18:29:59Z"), Kolkata));
        Assert.Equal(new DateOnly(2026, 10, 21), BusinessCalendar.DateOf(Utc("2026-10-20T18:30:00Z"), Kolkata));
    }

    [Fact]
    public void StartOf_IsTheUtcInstantTheZoneDayBegins() =>
        Assert.Equal(Utc("2026-03-31T18:30:00Z"), BusinessCalendar.StartOf(new DateOnly(2026, 4, 1), Kolkata));

    [Fact]
    public void ZoneOverloads_AgreeWithTheFixedIstOverloads()
    {
        var instant = Utc("2026-10-20T20:30:00Z");
        Assert.Equal(BusinessCalendar.DateOf(instant), BusinessCalendar.DateOf(instant, Kolkata));
        var day = new DateOnly(2026, 10, 21);
        Assert.Equal(BusinessCalendar.StartOf(day), BusinessCalendar.StartOf(day, Kolkata));
    }

    [Fact]
    public void Resolve_UnknownZone_Throws() =>
        Assert.Throws<TimeZoneNotFoundException>(() => BusinessTimeZones.Resolve("Mars/Olympus"));
}
