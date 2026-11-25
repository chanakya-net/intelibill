using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0604Tests
{
    [Fact]
    public async Task ReleaseAfterAnAcknowledgedSuspensionStaysPendingUntilTheMerchantConfirmsIt()
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(fixture);
        var shop = p.Shops[2];
        var delivery = fixture.Checkout.Delivery;
        var (assignment, security) = await SuspendAcknowledgedAssignmentAsync(fixture, p, term, shop);
        await ReleaseAsync(security, p.Account, assignment.Restriction);

        // Release queued: the merchant still holds the acknowledged suspension, so the payer cannot read Active.
        await AssertViewsAsync(p, term, shop, assignment.Id, activation: "Applying", receipt: "Applied", owner: "Suspended");

        // Only the merchant's local application has committed.
        await delivery.DrainAsync(true);
        await AssertViewsAsync(p, term, shop, assignment.Id, activation: "Applying", receipt: "Applied", owner: "AppliedLocally");

        // The return receipt for the release confirms the usable state on both views.
        await delivery.DrainAsync(false);
        await AssertViewsAsync(p, term, shop, assignment.Id, activation: "Acknowledged", receipt: "Applied", owner: "Acknowledged");
    }

    [Fact]
    public async Task DeadLetteredReleaseNeedsReviewUntilASupersedingReceiptConfirmsAccess()
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(fixture);
        var shop = p.Shops[2];
        var delivery = fixture.Checkout.Delivery;
        var (assignment, security) = await SuspendAcknowledgedAssignmentAsync(fixture, p, term, shop);
        await ReleaseAsync(security, p.Account, assignment.Restriction);
        var release = await LatestSnapshotAsync(fixture, shop.ShopId);
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            UPDATE entitlement_outbox SET status = 'DeadLetter', last_failure = 'Unavailable' WHERE event_id = '{release}'
            """);

        // The earlier activation receipt cannot hide the failed release.
        await AssertViewsAsync(p, term, shop, assignment.Id, activation: "NeedsReview", receipt: "DeadLetter", owner: "NeedsReview");

        // A later snapshot the merchant applies supersedes the lost release and confirms access.
        await P0403Tests.QueueReconciliationAsync(delivery, shop.ShopId);
        var superseding = await LatestSnapshotAsync(fixture, shop.ShopId);
        Assert.NotEqual(release, superseding);
        await DeliverSingleSnapshotAsync(fixture, superseding, expectOutcome: "Applied");
        await AssertViewsAsync(p, term, shop, assignment.Id, activation: "Applying", receipt: "Applied", owner: "AppliedLocally");
        await delivery.DrainAsync(false);
        await AssertViewsAsync(p, term, shop, assignment.Id, activation: "Acknowledged", receipt: "Applied", owner: "Acknowledged");
    }

    private static async Task<((Guid Id, Guid Restriction) Assignment, P0110CookieBrowser Security)>
        SuspendAcknowledgedAssignmentAsync(P0308Fixture fixture, Package p, Guid term, P0209Fixture.Merchant shop)
    {
        var delivery = fixture.Checkout.Delivery;
        var assignment = await AcceptSlotAsync(p, term, shop);
        await delivery.DrainAsync(false);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        await AssertViewsAsync(p, term, shop, assignment, activation: "Acknowledged", receipt: "Applied", owner: "Acknowledged");

        var security = await P0403Tests.SignInSecurityAsync(delivery);
        var restriction = await P0403Tests.ApplySuspensionAsync(security, p.Account, "Shop", shop.ShopId, "Trading", February14);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        await AssertViewsAsync(p, term, shop, assignment, activation: "Suspended", receipt: "Applied", owner: "Suspended");
        return ((assignment, restriction), security);
    }

    private static async Task ReleaseAsync(P0110CookieBrowser security, Guid account, Guid restriction)
    {
        var path = $"/api/admin/accounts/{account:D}/restrictions/{restriction:D}/release";
        using var released = await P0212Fixture.PostAsync(security, path, await P0404Tests.PreviewReleaseAsync(security, path));
        Assert.Equal(HttpStatusCode.Created, released.StatusCode);
    }

    private static async Task AssertViewsAsync(Package p, Guid term, P0209Fixture.Merchant shop, Guid assignment,
        string activation, string receipt, string owner)
    {
        var billing = await ReadAssignmentAsync(p.Payer.Client, Slots(p, term), assignment);
        var ownerView = await shop.Client.GetFromJsonAsync<JsonElement>(Consent(p, shop.ShopId, assignment));
        Assert.Equal((activation, receipt, owner), (billing.GetProperty("activation").GetString(),
            billing.GetProperty("merchantReceipt").GetString(), ownerView.GetProperty("status").GetString()));
    }
}
