using System.Net;
using static Intelibill.Licensing.Integration.Tests.P0417Tests;

namespace Intelibill.Licensing.Integration.Tests;

// Independent databases so the database-wide closure target count sees only this case.
public sealed class P0417TestsMigrations(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task ReopenedClosureEvidenceIsForcedRlsAndRetained()
    {
        var clock = new P0201Clock { Now = P0202Fixture.WholeSecondNow() };
        await using var fixture = await P0213Fixture.StartAsync(database, businessClock: clock);
        var (owner, _, route) = await CloseAsync(fixture, clock.Now);
        await fixture.DrainAsync(source: true);
        Assert.Equal("Closed", await fixture.MerchantScalarAsync($"SELECT state FROM shop_license_states WHERE shop_id = '{owner.ShopId:D}'"));

        using var admin = await SignInAsync(fixture);
        using var reopened = await P0212Fixture.PostAsync(admin, route, Command(await PreviewAsync(admin, route), owner));
        Assert.True(reopened.StatusCode == HttpStatusCode.Created, await reopened.Content.ReadAsStringAsync());
        await fixture.DrainAsync(source: true);
        await AssertRetentionAsync(fixture);
    }

    private static async Task AssertRetentionAsync(P0213Fixture fixture)
    {
        Assert.Equal(true, await fixture.SourceScalarAsync("""
            SELECT relforcerowsecurity FROM pg_class WHERE relname = 'operational_closure_reopenings'
            """));
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.Fixture.Runtime,
            "SELECT count(*) FROM public.operational_closure_reopenings"));
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.Fixture.Runtime,
            "SELECT count(*) FROM public.active_operational_closure_targets"));
        Assert.Equal(1L, await fixture.Trial.CountAsync("operational_closure_targets"));
    }
}
