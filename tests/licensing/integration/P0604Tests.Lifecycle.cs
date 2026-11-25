using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0604Tests
{
    [Theory]
    [InlineData(-1, "Applied")]
    [InlineData(0, "Refused")]
    public async Task OriginalPaidThroughIsExclusiveEvenForAnAcceptedReceipt(int microseconds, string expected)
    {
        await using var f = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(f);
        f.At(February29.AddMinutes(-1));
        await AcceptSlotAsync(p, term, p.Shops[2]);
        f.At(February29.AddTicks(10 * microseconds));
        await f.Checkout.Delivery.DrainAsync(false);
        Assert.Equal(expected, await f.ScalarAsync("SELECT state FROM coverage_slot_assignments"));
        Assert.Equal(expected == "Applied" ? 3L : 2L, await f.CountAsync("payment_fulfilment_shops"));
        if (expected == "Refused") Assert.Equal("TermEnded", await f.ScalarAsync("SELECT reason FROM coverage_slot_assignments"));
    }

    [Theory]
    [InlineData("revoke")]
    [InlineData("owner-version")]
    [InlineData("over-capacity")]
    public async Task ChangedOwnerConsentOrExcessLiveMembersPreventAcceptance(string change)
    {
        await using var f = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(f);
        var shop = p.Shops[2];
        var path = Consent(p, shop.ShopId, await BeginAsync(p, term, shop.ShopId));
        using var granted = await Decide(shop.Client, path, "grant");
        granted.EnsureSuccessStatusCode();
        if (change == "revoke")
        {
            using var revoked = await Decide(shop.Client, path, "revoke");
            revoked.EnsureSuccessStatusCode();
        }
        else if (change == "owner-version")
            await LicensingPostgreSqlFixture.ExecuteAsync(f.Checkout.Delivery.MerchantDb,
                $"UPDATE shop_memberships SET authority_version=authority_version+1 WHERE shop_id='{shop.ShopId}'");
        else
            for (var i = 0; i < 5; i++) await P0209Fixture.SignInAsync(f.Hosts, ShopRole.Staff, shop.ShopId);
        using var rejected = await Decide(shop.Client, path, "accept");
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(change == "over-capacity" ? "Coverage.MemberCapacityExceeded" : "Billing.ConsentInvalid",
            await P0206Fixture.CodeAsync(rejected));
        Assert.Equal(0L, await f.Checkout.Delivery.MerchantScalarAsync("SELECT count(*) FROM entitlement_messages WHERE kind='CoverageSlotConsent'"));
        Assert.Equal(2L, await f.CountAsync("payment_fulfilment_shops"));
    }

    [Fact]
    public async Task RecoveryIsActorBoundAndManagerRemovalBeforeReceiptRefusesCoverage()
    {
        await using var f = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(f);
        var manager = p.Shops[1];
        await LicensingPostgreSqlFixture.ExecuteAsync(f.Hosts.LicensingOwner, $"""
            INSERT INTO billing_account_members(id,account_id,merchant_user_id,role,status,revision,created_at)
            VALUES(gen_random_uuid(),'{p.Account}','{manager.UserId}',2,1,1,now())
            """);
        var shop = p.Shops[2];
        var path = Slots(p, term);
        var key = Guid.NewGuid();
        using var requested = await manager.Client.PostAsJsonAsync(path, Request(shop.ShopId, key));
        requested.EnsureSuccessStatusCode();
        var id = (await requested.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("assignmentId").GetGuid();
        using var wrongActor = await p.Payer.Client.GetAsync($"{path}/operations/{key}");
        Assert.Equal(HttpStatusCode.NotFound, wrongActor.StatusCode);
        using var recovered = await manager.Client.GetAsync($"{path}/operations/{key}");
        Assert.Equal(id, (await recovered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("assignmentId").GetGuid());
        using var mismatch = await manager.Client.PostAsJsonAsync(path, Request(p.Shops[3].ShopId, key));
        Assert.Equal("Billing.OperationPayloadMismatch", await P0206Fixture.CodeAsync(mismatch));
        using var noShopAccess = await manager.Client.PostAsJsonAsync("/api/shops/switch", new { shopId = shop.ShopId });
        Assert.Equal(HttpStatusCode.Forbidden, noShopAccess.StatusCode);
        var consent = Consent(p, shop.ShopId, id);
        using var granted = await Decide(shop.Client, consent, "grant");
        granted.EnsureSuccessStatusCode();
        using var accepted = await Decide(shop.Client, consent, "accept");
        accepted.EnsureSuccessStatusCode();
        await LicensingPostgreSqlFixture.ExecuteAsync(f.Hosts.LicensingOwner,
            $"UPDATE billing_account_members SET status=2,revision=revision+1 WHERE account_id='{p.Account}' AND merchant_user_id='{manager.UserId}'");
        await f.Checkout.Delivery.DrainAsync(false);
        Assert.Equal("RequesterChanged", await f.ScalarAsync("SELECT reason FROM coverage_slot_assignments"));
        Assert.Equal(2L, await f.CountAsync("payment_fulfilment_shops"));
        using var denied = await manager.Client.GetAsync($"{path}/operations/{key}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Theory]
    [InlineData("paid")]
    [InlineData("trial")]
    [InlineData("future-grant")]
    public async Task OverlappingCoverageAcquiredBeforeDeliveryRefusesTheSlot(string competingCoverage)
    {
        await using var f = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(f);
        var shop = p.Shops[2];
        var assignment = await AcceptSlotAsync(p, term, shop);
        switch (competingCoverage)
        {
            case "paid":
                await PurchaseAsync(p with { Shops = [shop] }, "Month");
                break;
            case "trial":
                await P0309Tests.ApplyTrialAsync(f, shop);
                break;
            default:
                await P0212Fixture.GrantAsync(f.Checkout.Trial.CatalogueManager!, p.Account,
                    P0212Fixture.Draft([shop.ShopId], February14.AddDays(1), February29));
                break;
        }
        await f.Checkout.Delivery.DrainAsync(false);
        Assert.Equal("ShopCovered", await f.ScalarAsync($"SELECT reason FROM coverage_slot_assignments WHERE assignment_id='{assignment}'"));
        Assert.Equal(2L, await f.CountAsync("payment_fulfilment_shops", $"term_id='{term}'"));
        Assert.Equal(0L, await f.CountAsync("coverage_slot_assignment_deliveries"));
    }

    [Fact]
    public async Task CoverageBeginsAtAssignmentAndRenewsWithOriginalPackage()
    {
        await using var f = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(f);
        var shop = p.Shops[2];
        await AcceptSlotAsync(p, term, shop);
        await f.Checkout.Delivery.DrainAsync(false);
        f.At(February14.AddTicks(-10));
        Assert.Equal(JsonValueKind.Null, (await ShopTermAsync(p, shop)).ValueKind);
        f.At(February14);
        Assert.Equal(term, (await ShopTermAsync(p, shop)).GetProperty("termId").GetGuid());
        var (quote, paid) = await RenewAsync(p, term, "Month");
        Assert.Equal(3, quote.ShopCount);
        Assert.Equal(February29, quote.PreviewStartsAt);
        Assert.Equal("Fulfilled", paid.GetProperty("fulfilment").GetString());
        Assert.Equal(3L, await f.CountAsync("payment_fulfilment_shops", $"term_id='{paid.GetProperty("termId").GetGuid()}'"));
    }

    [Fact]
    public async Task AdmissionsDuringDeliveryCannotActivateAnOverCapacityShop()
    {
        await using var f = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(f);
        var shop = p.Shops[2];
        var assignment = await AcceptSlotAsync(p, term, shop);
        await f.Checkout.Delivery.DrainAsync(false);
        for (var i = 0; i < 5; i++) await P0209Fixture.SignInAsync(f.Hosts, ShopRole.Staff, shop.ShopId);
        await f.Checkout.Delivery.DrainAsync(true);
        await f.Checkout.Delivery.DrainAsync(false);
        var page = await p.Payer.Client.GetFromJsonAsync<CoverageSlotTermV1>(Slots(p, term));
        Assert.Equal("NeedsReview", Assert.Single(page!.Assignments, a => a.AssignmentId == assignment).Activation);
        Assert.Equal(0L, await f.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM shop_license_states WHERE shop_id='{shop.ShopId}' AND term_id='{term}'"));
        Assert.Equal(6L, await f.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM shop_memberships WHERE shop_id='{shop.ShopId}' AND status='Active'"));
    }

    [Fact]
    public async Task RetainedRequestRefusesDowngrade()
    {
        await using var f = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(f);
        await BeginAsync(p, term, p.Shops[2].ShopId);
        await using var db = f.Hosts.Fixture.OpenMigrationContext();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.GetService<IMigrator>().MigrateAsync("20261124140000_ScheduledCapacityReductions"));
        Assert.Equal("55000", Assert.IsType<PostgresException>(ex.GetBaseException()).SqlState);
        Assert.Equal(1L, await f.CountAsync("coverage_slot_assignments"));
    }
}
