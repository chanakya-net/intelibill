using System.Globalization;
using Intelibill.Domain.Common;
using Intelibill.Infrastructure.Services.Time;

namespace Intelibill.Application.Unit.Tests.Common.Time;

public class BusinessClockTests
{
    private const string Zone = BusinessTimeZones.DefaultId;

    private static DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    private static BusinessClock At(string utc) => new(new FixedTimeProvider(Utc(utc)));

    [Fact]
    public void Today_ReadsTheShopZoneNotUtc() =>
        Assert.Equal(new DateOnly(2026, 10, 21), At("2026-10-20T20:30:00Z").Today(Zone));

    [Fact]
    public void GetBusinessDay_CarriesTheZone()
    {
        var day = At("2026-10-20T20:30:00Z").GetBusinessDay(Utc("2026-10-20T18:29:59Z"), Zone);
        Assert.Equal(new DateOnly(2026, 10, 20), day.Date);
        Assert.Equal(Zone, day.TimeZoneId);
    }

    [Fact]
    public void GetUtcWindow_SingleDay_IsHalfOpenOverTheZoneDay()
    {
        var window = At("2026-10-20T20:30:00Z").GetUtcWindow(new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 21), Zone);
        Assert.Equal(Utc("2026-10-20T18:30:00Z"), window.StartInclusive);
        Assert.Equal(Utc("2026-10-21T18:30:00Z"), window.EndExclusive);
        Assert.True(window.Contains(window.StartInclusive));
        Assert.False(window.Contains(window.EndExclusive));
    }

    [Fact]
    public void GetUtcWindow_Month_EndsAtTheStartOfTheNextZoneDay()
    {
        var window = At("2026-10-20T20:30:00Z").GetUtcWindow(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), Zone);
        Assert.Equal(Utc("2026-03-31T18:30:00Z"), window.EndExclusive);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
