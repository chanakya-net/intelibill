using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0215Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0215Tests
{
    [Fact]
    public async Task CancellationPropagatesOnlyToAcceptedShopsAndRetainsPriorConfirmation()
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

        var path = CancellationPath(account, term);
        var acceptance = Accept(await PreviewAsync(owner, path), Guid.NewGuid());
        var original = await fixture.LatestSnapshotAsync(owner.ShopId);
        var unrelated = await fixture.LatestSnapshotAsync(other.ShopId);
        Assert.Equal(end.AddDays(7), original.Shop!.AccessEndsAt);
        await AssertAtomicRollbackAsync(fixture, owner, path, acceptance, original.EventId);
        using var scheduled = await owner.Client.PostAsJsonAsync(path, acceptance);
        await ReadAsync(scheduled);
        await AssertDeliveryAsync(fixture, owner, end, original);
        Assert.Equal(unrelated, await fixture.LatestSnapshotAsync(other.ShopId));
        await P0209Tests.PostStockAsync(owner.Client, "Before cancellation boundary");
        fixture.Hosts.StatusClock.Now = end;
        Assert.Equal("Expired", (await StatusAsync(owner)).GetProperty("state").GetString());
        Assert.Equal("Grace", (await StatusAsync(other)).GetProperty("state").GetString());
        await P0209Tests.PostStockAsync(owner.Client, "After cancellation boundary");
        Assert.Equal(1L, await fixture.MerchantScalarAsync($"""
            SELECT count(*) FROM shop_license_states WHERE shop_id = '{owner.ShopId:D}' AND enforcement_activated_at IS NULL
            """));
    }

    private static async Task AssertAtomicRollbackAsync(P0213Fixture fixture, Merchant owner, string path, object command, Guid original)
    {
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, """
            ALTER TABLE public.entitlement_snapshots ADD CONSTRAINT refuse_cancellation_fixture CHECK (false) NOT VALID
            """);
        try
        {
            using var rejected = await owner.Client.PostAsJsonAsync(path, command);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
            foreach (var table in new[] { "scheduled_subscription_changes", "scheduled_subscription_change_shops", "subscription_cancellation_receipts" })
                Assert.Equal(0L, await fixture.Trial.CountAsync(table));
            Assert.Equal(original, (await fixture.LatestSnapshotAsync(owner.ShopId)).EventId);
        }
        finally
        {
            await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner,
                "ALTER TABLE public.entitlement_snapshots DROP CONSTRAINT refuse_cancellation_fixture");
        }
    }

    private static async Task AssertDeliveryAsync(P0213Fixture fixture, Merchant owner, DateTimeOffset end,
        Intelibill.Shared.Contracts.V1.EntitlementSnapshotV1 original)
    {
        var snapshot = await fixture.LatestSnapshotAsync(owner.ShopId);
        Assert.Equal(original.Account, snapshot.Account);
        Assert.Equal(original.Shop!.Revision + 1, snapshot.Shop!.Revision);
        Assert.Equal(end, snapshot.Shop.AccessEndsAt);
        Assert.Equal(end, snapshot.Shop.NextBoundary);
        var pending = (await StatusAsync(owner)).GetProperty("synchronization");
        Assert.Equal("Pending", pending.GetProperty("state").GetString());
        Assert.Equal(original.Shop.Revision, pending.GetProperty("appliedShopRevision").GetInt64());

        await fixture.DrainAsync(source: true);
        await P0214Tests.LoseAcknowledgementAsync(fixture, snapshot);
        var lost = (await StatusAsync(owner)).GetProperty("synchronization");
        Assert.True(JsonElement.DeepEquals(pending.GetProperty("lastSynchronizedAt"), lost.GetProperty("lastSynchronizedAt")));
        fixture.Transport.LeaseClock.Now += TimeSpan.FromSeconds(4);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);

        var (response, _) = await fixture.SendAsync(original);
        using (response) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var applied = (await StatusAsync(owner)).GetProperty("synchronization");
        Assert.Equal("Applied", applied.GetProperty("state").GetString());
        Assert.Equal(snapshot.Shop.Revision, applied.GetProperty("appliedShopRevision").GetInt64());
        Assert.Equal(snapshot.EventId, (await fixture.LatestSnapshotAsync(owner.ShopId)).EventId);
    }
}
