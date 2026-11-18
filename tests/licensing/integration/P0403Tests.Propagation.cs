using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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

    [Fact]
    public async Task LegacySecurityHeadPreventsFutureSuspensionFromReopeningAccess()
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        await using (var licensing = fixture.Hosts.Fixture.OpenMigrationContext())
        {
            var applied = (await licensing.Database.GetAppliedMigrationsAsync()).ToArray();
            await licensing.GetService<IMigrator>().MigrateAsync(applied[^2]);
        }

        var shop = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(shop.Client);
        await SeedLegacyHeadAsync(fixture, account);

        await using (var licensing = fixture.Hosts.Fixture.OpenMigrationContext())
        {
            await licensing.Database.MigrateAsync();
            await licensing.Database.ExecuteSqlRawAsync("""
                GRANT SELECT, INSERT ON public.operational_restrictions,
                    public.operational_restriction_deliveries TO licensing_runtime
                """);
        }

        await LinkIndependentAsync(fixture, account, shop, shop);
        await fixture.DrainAsync(source: true);
        await EnrollAsync(fixture, shop);
        await AssertLegacySecurityAsync(shop);
        using var security = await SignInSecurityAsync(fixture);
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        using var preview = await P0212Fixture.PostAsync(security, path + "/preview", new { scope = "Account" });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var impact = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(impact.GetProperty("alreadySuspended").GetBoolean());
        Assert.Equal(7, impact.GetProperty("version").GetInt64());
        using var save = await P0212Fixture.PostAsync(security, path, new
        {
            scope = "Account", mode = "Trading", operationKey = Guid.NewGuid(), expectedVersion = 7,
            effectiveAt = fixture.Hosts.StatusClock.Now.AddMinutes(10), impactDigest = impact.GetProperty("impactDigest").GetString(),
            customerExplanation = "Customer explanation", internalReason = "Internal evidence", reviewReference = "CASE-878",
        });
        Assert.Equal(HttpStatusCode.Conflict, save.StatusCode);
        Assert.Equal("Suspension.AlreadySuspended", await P0206Fixture.CodeAsync(save));
        Assert.Equal(0L, await fixture.Trial.CountAsync("operational_restrictions"));
        await AssertLegacySecurityAsync(shop);

        await QueueReconciliationAsync(fixture, shop.ShopId);
        await fixture.DrainAsync(source: true);
        var reconciled = await fixture.LatestSnapshotAsync(shop.ShopId);
        Assert.Equal("Security", reconciled.Account!.Restriction);
        Assert.Null(reconciled.Account.Suspension);
        await AssertLegacySecurityAsync(shop);
    }

    private static async Task SeedLegacyHeadAsync(P0213Fixture fixture, Guid account)
    {
        await using var db = fixture.Hosts.Fixture.OpenMigrationContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.billing_account_id', {account.ToString("D")}, true);
            INSERT INTO entitlement_account_heads (account_id, revision, upstream_verified_at, restriction)
            VALUES ({account}, 7, {fixture.Hosts.StatusClock.Now}, 'Security')
            """);
        await transaction.CommitAsync();
    }

    private static async Task QueueReconciliationAsync(P0213Fixture fixture, Guid shop)
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
