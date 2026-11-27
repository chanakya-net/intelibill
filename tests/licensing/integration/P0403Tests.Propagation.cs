using System.Net;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0403Tests
{
    [Fact]
    public async Task NewAffiliationsAndLaterSignedConfirmationUpdateSavedPropagation()
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var first = await CreateTradingShopAsync(fixture.Hosts);
        var second = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(first.Client);
        using var security = await SignInSecurityAsync(fixture);
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        var saved = await ApplySuspensionAsync(security, account, "Account", null, "Trading");
        await AssertPropagationAsync(security, path, saved, "NoShops");

        await LinkIndependentAsync(fixture, account, first, first);
        await AssertPropagationAsync(security, path, saved, "Pending");
        await fixture.DrainAsync(source: true);
        await AssertPropagationAsync(security, path, saved, "Pending");
        await fixture.DrainAsync(source: false);
        await AssertPropagationAsync(security, path, saved, "Acknowledged");

        await LinkIndependentAsync(fixture, account, first, second);
        await AssertPropagationAsync(security, path, saved, "Pending");
        var failed = await fixture.LatestSnapshotAsync(second.ShopId);
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            UPDATE entitlement_outbox SET status = 'DeadLetter', last_failure = 'Unavailable'
            WHERE event_id = '{failed.EventId:D}'
            """);
        var review = await AssertPropagationAsync(security, path, saved, "NeedsReview");
        Assert.Equal("Acknowledged", ShopPropagation(review, first.ShopId));

        await QueueReconciliationAsync(fixture, second.ShopId);
        var newer = await fixture.LatestSnapshotAsync(second.ShopId);
        Assert.True(newer.Shop!.Revision > failed.Shop!.Revision);
        Assert.Equal(failed.Account!.Suspension, newer.Account!.Suspension);
        await fixture.DrainAsync(source: true);
        await AssertPropagationAsync(security, path, saved, "Pending");
        await fixture.DrainAsync(source: false);
        var applied = await AssertPropagationAsync(security, path, saved, "Acknowledged");
        Assert.Equal("Acknowledged", ShopPropagation(applied, second.ShopId));
        Assert.Equal("DeadLetter", await fixture.SourceScalarAsync(
            $"SELECT status FROM entitlement_outbox WHERE event_id = '{failed.EventId:D}'"));
    }

    private static async Task AssertLegacySecurityAsync(Merchant shop)
    {
        using var authority = await ReadAuthorityAsync(shop);
        Assert.Equal("Licence.SecuritySuspended", await P0206Fixture.CodeAsync(authority));
        foreach (var path in new[] { "/api/sales", "/api/exports/sales" })
        {
            using var read = await shop.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
            Assert.Equal("Licence.SecuritySuspended", await P0206Fixture.CodeAsync(read));
        }
    }

    internal static async Task QueueReconciliationAsync(P0213Fixture fixture, Guid shop)
    {
        // Make this shop due without advancing the fixed identity or business clocks.
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            UPDATE entitlement_shop_heads SET revision = revision + 1,
                next_verification_at = timestamptz '2000-01-01T00:00:00Z'
            WHERE shop_id = '{shop:D}'
            """);
        await fixture.ScanAsync(source: true);
    }

    private static string? ShopPropagation(JsonElement status, Guid id) =>
        status.GetProperty("shops").EnumerateArray().Single(row => row.GetProperty("shopId").GetGuid() == id)
            .GetProperty("status").GetString();
}
