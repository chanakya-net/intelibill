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
    [Fact]
    public async Task BillingAndOwnerViewsSeparateMerchantReceiptFromCurrentAccessAcrossLifecycle()
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, orderId, term) = await PrepareRefundableAsync(fixture);
        var shop = p.Shops[2];
        var delivery = fixture.Checkout.Delivery;
        var assignment = await AcceptSlotAsync(p, term, shop);
        var consentPath = Consent(p, shop.ShopId, assignment);
        var slotsPath = Slots(p, term);

        // Stage 1: licensing applied assignment, BEFORE merchant delivery.
        await delivery.DrainAsync(false);
        var s1Billing = await ReadAssignmentAsync(p.Payer.Client, slotsPath, assignment);
        var s1Owner = await shop.Client.GetFromJsonAsync<JsonElement>(consentPath);
        Assert.Equal("Applying", s1Billing.GetProperty("activation").GetString());
        Assert.Equal("Pending", s1Billing.GetProperty("merchantReceipt").GetString());
        Assert.Equal("Applying", s1Owner.GetProperty("status").GetString());

        // Stage 2: merchant commits projection, BEFORE return receipt reaches licensing.
        await delivery.DrainAsync(true);
        var s2Billing = await ReadAssignmentAsync(p.Payer.Client, slotsPath, assignment);
        var s2Owner = await shop.Client.GetFromJsonAsync<JsonElement>(consentPath);
        Assert.Equal("Applying", s2Billing.GetProperty("activation").GetString());
        Assert.Equal("Pending", s2Billing.GetProperty("merchantReceipt").GetString());
        Assert.Equal("AppliedLocally", s2Owner.GetProperty("status").GetString());

        // Stage 3: licensing consumes the return receipt -> both views report Acknowledged + Applied receipt.
        await delivery.DrainAsync(false);
        var s3Billing = await ReadAssignmentAsync(p.Payer.Client, slotsPath, assignment);
        var s3Owner = await shop.Client.GetFromJsonAsync<JsonElement>(consentPath);
        Assert.Equal("Acknowledged", s3Billing.GetProperty("activation").GetString());
        Assert.Equal("Applied", s3Billing.GetProperty("merchantReceipt").GetString());
        Assert.Equal("Acknowledged", s3Owner.GetProperty("status").GetString());

        // Later shop suspension -> receipt stays Applied, current activation becomes Suspended on both views!
        using var security = await P0403Tests.SignInSecurityAsync(delivery, "billing");
        var restrictionId = await P0403Tests.ApplySuspensionAsync(security, p.Account, "Shop", shop.ShopId, "Trading", February14);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        var suspBilling = await ReadAssignmentAsync(p.Payer.Client, slotsPath, assignment);
        var suspOwner = await shop.Client.GetFromJsonAsync<JsonElement>(consentPath);
        Assert.Equal("Suspended", suspBilling.GetProperty("activation").GetString());
        Assert.Equal("Applied", suspBilling.GetProperty("merchantReceipt").GetString());
        Assert.Equal("Suspended", suspOwner.GetProperty("status").GetString());

        // Release the suspension via the real release API, then shorten the term with EndAt -> both views report ended (not Acknowledged).
        var releasePath = $"/api/admin/accounts/{p.Account:D}/restrictions/{restrictionId:D}/release";
        var releaseCmd = await P0404Tests.PreviewReleaseAsync(security, releasePath);
        using var released = await P0212Fixture.PostAsync(security, releasePath, releaseCmd);
        Assert.Equal(HttpStatusCode.Created, released.StatusCode);
        await ApproveRefundAsync(fixture, p, orderId, "EndAt", February20, billingOperator: security);
        SyncAt(fixture, February20);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        var endBilling = await ReadAssignmentAsync(p.Payer.Client, slotsPath, assignment);
        var endOwner = await shop.Client.GetFromJsonAsync<JsonElement>(consentPath);
        Assert.Equal(February29, endBilling.GetProperty("coverageEndsAt").GetDateTimeOffset());
        Assert.Equal("TermEnded", endBilling.GetProperty("activation").GetString());
        Assert.Equal("Applied", endBilling.GetProperty("merchantReceipt").GetString());
        Assert.Equal("Expired", endOwner.GetProperty("status").GetString());
    }

    [Fact]
    public async Task OwnerViewShowsNeedsReviewOnExcessSeatsAndStaysAcknowledgedAcrossOutOfOrderEarlyRenewal()
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(fixture);
        var shop = p.Shops[2];
        var delivery = fixture.Checkout.Delivery;
        var assignment = await AcceptSlotAsync(p, term, shop);
        var consentPath = Consent(p, shop.ShopId, assignment);
        var slotsPath = Slots(p, term);
        await delivery.DrainAsync(false);

        // Exceed member allowance -> deliver snapshot -> merchant rejects (NeedsReview).
        var extra = new List<Guid>();
        for (var i = 0; i < 5; i++)
            extra.Add((await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Staff, shop.ShopId)).UserId);
        await delivery.DrainAsync(true);

        // Even BEFORE return receipt reaches licensing, owner sees NeedsReview (not Accepted indefinitely).
        Assert.Equal("NeedsReview", (await shop.Client.GetFromJsonAsync<JsonElement>(consentPath)).GetProperty("status").GetString());
        await delivery.DrainAsync(false);
        Assert.Equal("NeedsReview", (await ReadAssignmentAsync(p.Payer.Client, slotsPath, assignment)).GetProperty("activation").GetString());
        Assert.Equal("NeedsReview", (await shop.Client.GetFromJsonAsync<JsonElement>(consentPath)).GetProperty("status").GetString());

        // Resolve seats, then perform an early renewal (T2) and deliver T2 BEFORE any older T1 snapshot.
        await SuspendOneStaffAsync(shop, extra[0]);
        await RenewAsync(p, term, "Month");
        var latestId = (Guid)(await fixture.ScalarAsync($"""
            SELECT event_id FROM entitlement_snapshots WHERE shop_id='{shop.ShopId}' ORDER BY shop_revision DESC LIMIT 1
            """))!;
        await DeliverSingleSnapshotAsync(fixture, latestId, expectOutcome: "Applied");
        await delivery.DrainAsync(false);

        // Both billing and owner views report Acknowledged across the early renewal (never reverting to Applying).
        var billing = await ReadAssignmentAsync(p.Payer.Client, slotsPath, assignment);
        var owner = await shop.Client.GetFromJsonAsync<JsonElement>(consentPath);
        Assert.Equal("Acknowledged", billing.GetProperty("activation").GetString());
        Assert.Equal("Applied", billing.GetProperty("merchantReceipt").GetString());
        Assert.Equal("Acknowledged", owner.GetProperty("status").GetString());
    }

    private static async Task<JsonElement> ReadAssignmentAsync(HttpClient client, string slotsPath, Guid assignmentId)
    {
        var page = await client.GetFromJsonAsync<JsonElement>(slotsPath);
        return page.GetProperty("assignments").EnumerateArray()
            .Single(a => a.GetProperty("assignmentId").GetGuid() == assignmentId);
    }
}
