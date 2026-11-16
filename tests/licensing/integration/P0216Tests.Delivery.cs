using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0215Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0216Tests
{
    [Fact]
    public async Task WithdrawalRepublishesOriginalBoundaryToAcceptedShopsOnly()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var other = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        var (term, end) = await SeedCurrentTermAsync(fixture, account);
        var (otherTerm, _) = await SeedCurrentTermAsync(fixture, account);
        await P0213Fixture.LinkAsync(account, owner, owner, term);
        await P0213Fixture.LinkAsync(account, owner, other, otherTerm);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        var original = await fixture.LatestSnapshotAsync(owner.ShopId);
        Assert.Equal(end.AddDays(7), original.Shop!.AccessEndsAt);

        var cancellation = await ScheduleAsync(owner, account, term);
        var scheduled = await fixture.LatestSnapshotAsync(owner.ShopId);
        Assert.Equal(end, scheduled.Shop!.AccessEndsAt);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        var unrelated = await fixture.LatestSnapshotAsync(other.ShopId);
        var command = Withdraw(cancellation, Guid.NewGuid());
        await AssertWithdrawalRollsBackAsync(fixture, owner, account, term, command, scheduled.EventId);

        using (var withdrawn = await owner.Client.PostAsJsonAsync(WithdrawalPath(account, term), command))
            await ReadAsync(withdrawn);
        var snapshot = await fixture.LatestSnapshotAsync(owner.ShopId);
        Assert.Equal(scheduled.Shop.Revision + 1, snapshot.Shop!.Revision);
        Assert.Equal(end.AddDays(7), snapshot.Shop.AccessEndsAt);
        Assert.Equal(end, snapshot.Shop.NextBoundary);
        Assert.Equal(original.Account, snapshot.Account);
        Assert.Equal(unrelated, await fixture.LatestSnapshotAsync(other.ShopId));
        Assert.Equal("Pending", (await StatusAsync(owner)).GetProperty("synchronization").GetProperty("state").GetString());

        await fixture.DrainAsync(source: true);
        await P0214Tests.LoseAcknowledgementAsync(fixture, snapshot);
        fixture.Transport.LeaseClock.Now += TimeSpan.FromSeconds(4);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        var applied = (await StatusAsync(owner)).GetProperty("synchronization");
        Assert.Equal("Applied", applied.GetProperty("state").GetString());
        Assert.Equal(snapshot.Shop.Revision, applied.GetProperty("appliedShopRevision").GetInt64());
        fixture.Hosts.StatusClock.Now = end;
        Assert.Equal("Grace", (await StatusAsync(owner)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task WithdrawalAndRetriesNeverReleaseSuspension()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        var (term, _) = await SeedCurrentTermAsync(fixture, account);
        await P0213Fixture.LinkAsync(account, owner, owner, term);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        var cancellation = await ScheduleAsync(owner, account, term);
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            UPDATE public.entitlement_account_heads SET revision = revision + 1, restriction = 'Trading'
            WHERE account_id = '{account:D}'
            """);
        await fixture.ScanAsync(source: false);
        await DrainBothAsync(fixture);
        var head = await AccountHeadAsync(fixture, account);
        Assert.Contains("Trading", head, StringComparison.Ordinal);
        var before = await fixture.LatestSnapshotAsync(owner.ShopId);

        var command = Withdraw(cancellation, Guid.NewGuid());
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await owner.Client.PostAsJsonAsync(WithdrawalPath(account, term), command);
            await ReadAsync(response);
        }

        var after = await fixture.LatestSnapshotAsync(owner.ShopId);
        Assert.Equal(before.Shop!.Revision + 1, after.Shop!.Revision);
        Assert.Equal((long)(await fixture.SourceScalarAsync($"""
            SELECT revision FROM public.entitlement_account_heads WHERE account_id = '{account:D}'
            """))!, after.Account!.Revision);
        Assert.Equal("Trading", after.Account.Restriction);
        Assert.Null(after.Account.ReleasedRevision);
        Assert.Equal(head, await AccountHeadAsync(fixture, account));
        await DrainBothAsync(fixture);
        Assert.Equal("Trading", await fixture.MerchantScalarAsync($"""
            SELECT restriction FROM public.account_license_restrictions WHERE account_id = '{account:D}'
            """));
    }

    private static async Task DrainBothAsync(P0213Fixture fixture)
    {
        await fixture.DrainAsync(source: false);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
    }

    private static async Task<string> AccountHeadAsync(P0213Fixture fixture, Guid account) =>
        (string)(await fixture.SourceScalarAsync($"""
            SELECT to_jsonb(h)::text FROM public.entitlement_account_heads h WHERE account_id = '{account:D}'
            """))!;

    private static async Task AssertWithdrawalRollsBackAsync(P0213Fixture fixture, Merchant owner, Guid account, Guid term,
        object command, Guid latest)
    {
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, """
            ALTER TABLE public.entitlement_snapshots ADD CONSTRAINT refuse_withdrawal_fixture CHECK (false) NOT VALID
            """);
        try
        {
            using var rejected = await owner.Client.PostAsJsonAsync(WithdrawalPath(account, term), command);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
            Assert.Equal(1L, await fixture.Trial.CountAsync("scheduled_subscription_changes"));
            Assert.Equal(1L, await fixture.Trial.CountAsync("subscription_cancellation_receipts"));
            Assert.Equal(latest, (await fixture.LatestSnapshotAsync(owner.ShopId)).EventId);
        }
        finally
        {
            await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner,
                "ALTER TABLE public.entitlement_snapshots DROP CONSTRAINT refuse_withdrawal_fixture");
        }
    }
}
