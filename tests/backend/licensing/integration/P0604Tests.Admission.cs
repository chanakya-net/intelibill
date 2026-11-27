using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0604Tests
{
    [Theory]
    [InlineData("future-shop-suspension")]
    [InlineData("future-account-suspension")]
    [InlineData("first-usable-grace")]
    [InlineData("out-of-order-early-renewal")]
    public async Task FirstUsableProjectionUnderContentionEnforcesLiveSeatAllowanceAndRecovers(string scenario)
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(fixture);
        var shop = p.Shops[2];
        var delivery = fixture.Checkout.Delivery;
        var assignment = await AcceptSlotAsync(p, term, shop);
        await delivery.DrainAsync(false);

        // Admit 5 staff while snapshot is in flight -> 6 active members (> allowance 5).
        var addedStaff = new List<Guid>();
        for (var i = 0; i < 5; i++)
            addedStaff.Add((await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Staff, shop.ShopId)).UserId);

        var initialEventId = (Guid)(await fixture.ScalarAsync(
            $"SELECT event_id FROM coverage_slot_assignment_deliveries WHERE assignment_id='{assignment}'"))!;

        if (scenario is "future-shop-suspension" or "future-account-suspension")
        {
            using var security = await P0403Tests.SignInSecurityAsync(delivery);
            await P0403Tests.ApplySuspensionAsync(security, p.Account,
                scenario == "future-shop-suspension" ? "Shop" : "Account",
                scenario == "future-shop-suspension" ? shop.ShopId : null,
                "Trading", February14.AddHours(2));
        }
        else if (scenario == "first-usable-grace")
        {
            // Advance to exact paid-through so the shop enters usable Grace before first projection applies.
            SyncAt(fixture, February29);
            await delivery.ScanAsync(true);
        }
        else
        {
            // Early renewal creates successor term T2 and queues a higher-revision snapshot BEFORE T1 delivers.
            await RenewAsync(p, term, "Month");
            // Deliver ONLY the newest (early-renewal T2) snapshot first, skipping the original T1 snapshot!
            var latestId = (Guid)(await fixture.ScalarAsync($"""
                SELECT event_id FROM entitlement_snapshots WHERE shop_id='{shop.ShopId}' ORDER BY shop_revision DESC LIMIT 1
                """))!;
            Assert.NotEqual(initialEventId, latestId);
            await DeliverSingleSnapshotAsync(fixture, latestId, expectOutcome: "NeedsReview");
            // Exact redelivery of the rejected out-of-order snapshot replays NeedsReview idempotently.
            await DeliverSingleSnapshotAsync(fixture, latestId, expectOutcome: "NeedsReview");
            await AssertNoUsablePermissionAsync(fixture, p, shop, term);

            // Reduce active seats to 5, reconcile and verify clean recovery + exact redelivery.
            await SuspendOneStaffAsync(shop, addedStaff[0]);
            await P0403Tests.QueueReconciliationAsync(delivery, shop.ShopId);
            await delivery.DrainAsync(false);
            await delivery.DrainAsync(true);
            await delivery.DrainAsync(false);
            var recoveredId = (Guid)(await fixture.ScalarAsync($"""
                SELECT event_id FROM entitlement_snapshots WHERE shop_id='{shop.ShopId}' ORDER BY shop_revision DESC LIMIT 1
                """))!;
            await DeliverSingleSnapshotAsync(fixture, recoveredId, expectOutcome: "Applied");
            var pageAfter = await p.Payer.Client.GetFromJsonAsync<CoverageSlotTermV1>(Slots(p, term));
            Assert.Equal("Acknowledged", Assert.Single(pageAfter!.Assignments, a => a.AssignmentId == assignment).Activation);
            Assert.Equal("Active", (string)(await delivery.MerchantScalarAsync(
                $"SELECT state FROM shop_license_states WHERE shop_id='{shop.ShopId}'"))!);
            return;
        }

        // Deliver the newest snapshot (future suspension or first usable grace) directly to assert NeedsReview under contention.
        var targetEventId = (Guid)(await fixture.ScalarAsync($"""
            SELECT event_id FROM entitlement_snapshots WHERE shop_id='{shop.ShopId}' ORDER BY shop_revision DESC LIMIT 1
            """))!;
        await DeliverSingleSnapshotAsync(fixture, targetEventId, expectOutcome: "NeedsReview");
        await delivery.DrainAsync(false);
        await AssertNoUsablePermissionAsync(fixture, p, shop, term);
        var blocked = await p.Payer.Client.GetFromJsonAsync<CoverageSlotTermV1>(Slots(p, term));
        Assert.Equal("NeedsReview", Assert.Single(blocked!.Assignments, a => a.AssignmentId == assignment).Activation);

        // Reduce seats to 5, trigger a fresh snapshot, verify recovery AND exact redelivery.
        await SuspendOneStaffAsync(shop, addedStaff[0]);
        await P0403Tests.QueueReconciliationAsync(delivery, shop.ShopId);
        await delivery.DrainAsync(false);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        var okEventId = (Guid)(await fixture.ScalarAsync($"""
            SELECT event_id FROM entitlement_snapshots WHERE shop_id='{shop.ShopId}' ORDER BY shop_revision DESC LIMIT 1
            """))!;
        await DeliverSingleSnapshotAsync(fixture, okEventId, expectOutcome: "Applied");
        var expectedState = scenario == "first-usable-grace" ? "Grace" : "Active";
        Assert.Equal(expectedState, (string)(await delivery.MerchantScalarAsync(
            $"SELECT state FROM shop_license_states WHERE shop_id='{shop.ShopId}'"))!);
    }

    [Fact]
    public async Task ImmediateRestrictiveSuspensionStillAppliesWhenOverCapacity()
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(fixture);
        var shop = p.Shops[2];
        var delivery = fixture.Checkout.Delivery;
        await AcceptSlotAsync(p, term, shop);
        await delivery.DrainAsync(false);

        for (var i = 0; i < 5; i++)
            await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Staff, shop.ShopId);

        using var security = await P0403Tests.SignInSecurityAsync(delivery);
        var restrictionId = await P0403Tests.ApplySuspensionAsync(security, p.Account, "Shop", shop.ShopId, "Trading", February14);
        var targetEventId = (Guid)(await fixture.ScalarAsync($"""
            SELECT event_id FROM entitlement_snapshots WHERE shop_id='{shop.ShopId}' ORDER BY shop_revision DESC LIMIT 1
            """))!;
        await DeliverSingleSnapshotAsync(fixture, targetEventId, expectOutcome: "Applied");
        Assert.Equal("Trading", (string)(await delivery.MerchantScalarAsync(
            $"SELECT suspension_mode FROM shop_license_states WHERE shop_id='{shop.ShopId}'"))!);
        Assert.Equal(0L, await delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM coverage_slot_activations WHERE shop_id='{shop.ShopId}'"));

        // Releasing the suspension while still over-capacity must now enforce live seat allowance (NeedsReview).
        var releasePath = $"/api/admin/accounts/{p.Account:D}/restrictions/{restrictionId:D}/release";
        var releaseCmd = await P0404Tests.PreviewReleaseAsync(security, releasePath);
        using var released = await P0212Fixture.PostAsync(security, releasePath, releaseCmd);
        Assert.Equal(HttpStatusCode.Created, released.StatusCode);
        var releasedEventId = (Guid)(await fixture.ScalarAsync($"""
            SELECT event_id FROM entitlement_snapshots WHERE shop_id='{shop.ShopId}' ORDER BY shop_revision DESC LIMIT 1
            """))!;
        await DeliverSingleSnapshotAsync(fixture, releasedEventId, expectOutcome: "NeedsReview");
    }

    private static async Task DeliverSingleSnapshotAsync(P0308Fixture fixture, Guid eventId, string expectOutcome)
    {
        var delivery = fixture.Checkout.Delivery;
        var bytes = (byte[])(await fixture.ScalarAsync($"SELECT payload FROM entitlement_messages WHERE event_id='{eventId}'"))!;
        using var request = await delivery.Transport.SignedAsync(bytes, delivery.AuthClock, merchant: false,
            path: EntitlementSnapshotsV1.SnapshotPath);
        using var response = await delivery.MerchantApi.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ack = await response.Content.ReadFromJsonAsync<EntitlementAcknowledgementV1>(EntitlementSnapshotsV1.Json);
        Assert.Equal(expectOutcome, ack!.Outcome);
    }

    private static async Task AssertNoUsablePermissionAsync(P0308Fixture fixture, Package p, P0209Fixture.Merchant shop, Guid term)
    {
        var savedTerm = await fixture.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT term_id FROM shop_license_states WHERE shop_id='{shop.ShopId}'");
        Assert.True(savedTerm is null or DBNull || (Guid)savedTerm != term);
        Assert.Equal(6L, await fixture.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM shop_memberships WHERE shop_id='{shop.ShopId}' AND status='Active'"));
    }

    private static async Task SuspendOneStaffAsync(P0209Fixture.Merchant shop, Guid userId)
    {
        using var patch = await P0603Tests.UpdateMemberStatusAsync(shop, userId, "Suspended", expectedVersion: 0);
        Assert.True(patch.IsSuccessStatusCode, await patch.Content.ReadAsStringAsync());
    }
}
