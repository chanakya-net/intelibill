using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0404Tests
{
    [Fact]
    public async Task CommercialWritersAndReconciliationRespectExplicitRelease()
    {
        await using var fixture = await P0308Fixture.StartAsync(database, P0308Tests.March31);
        var package = await fixture.PackageAsync(1);
        var delivery = fixture.Checkout.Delivery;
        using var admin = await SignInSecurityAsync(delivery);
        var path = $"/api/admin/accounts/{package.Account:D}/restrictions";
        var shop = package.Payer.ShopId;
        var id = await SuspendAsync(admin, path, "Shop", shop);
        var (_, term) = await P0308Fixture.PurchaseAsync(package, "Month");
        await AssertStillSuspendedAsync(delivery, shop);
        var (_, renewal) = await P0308Fixture.RenewAsync(package, term, "Month");
        AssertStatus(renewal, "Verified", "Fulfilled", "Applying");
        Assert.Equal(2L, await fixture.CountAsync("payment_fulfilments"));
        await ReconcileAsync(delivery, shop);
        await AssertStillSuspendedAsync(delivery, shop);

        fixture.At(P0308Tests.April30.AddDays(1));
        var third = await P0308Fixture.AcceptAsync(package,
            await P0308Fixture.RenewalQuoteAsync(package, renewal.GetProperty("termId").GetGuid(), "Month"));
        var attempt = await P0308Fixture.StartPaymentAsync(package, third);
        await CaptureAsync(package.Payer.Client, third, attempt);
        var releasePath = $"{path}/{id:D}/release";
        var command = await PreviewReleaseAsync(admin, releasePath);
        var review = P0212Fixture.PostAsync(admin, releasePath, command);
        var verify = VerifyAsync(package.Payer.Client, third, attempt);
        using var released = await review;
        AssertStatus(await verify, "Verified", "Fulfilled", "Applying");
        // The renewal can change the reviewed term before the release gets the lock.
        if (released.StatusCode == HttpStatusCode.Conflict) await SubmitReleaseAsync(admin, releasePath);
        else Assert.True(released.StatusCode == HttpStatusCode.Created, await released.Content.ReadAsStringAsync());
        var saved = await delivery.LatestSnapshotAsync(shop);
        Assert.Null(saved.Shop!.Suspension);
        Assert.NotNull(saved.Shop.ReleasedRevision);
        await ReconcileAsync(delivery, shop);
        Assert.Equal(saved.Shop.ReleasedRevision, (await delivery.LatestSnapshotAsync(shop)).Shop!.ReleasedRevision);
        Assert.Equal(1L, await fixture.CountAsync("operational_restriction_releases"));
    }

    [Fact]
    public async Task RefundedObservationAndReviewDoNotReleaseAccountHold()
    {
        await using var rig = await P0304Tests.Rig.StartAsync(database);
        var fixture = rig.Fixture.Delivery;
        var account = rig.Quote.AccountId;
        using var admin = await SignInSecurityAsync(fixture, "billing");
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        await SuspendAsync(admin, path, "Account", null);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        rig.Fake.AddPayment(order, "captured");
        var duplicate = rig.Fake.AddPayment(order, "refunded");
        (rig.Fake.Payment(duplicate)["amount_refunded"], rig.Fake.Payment(duplicate)["refund_status"]) =
            (rig.Quote.AmountPaise, "full");
        await rig.VerifyAsync(attempt, null, null);
        Assert.Equal(1L, await rig.Fixture.CountAsync("payment_restoration_holds"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("operational_restriction_releases"));

        var cases = $"/api/admin/accounts/{account:D}/payment-reviews";
        using var list = await admin.SendAsync(HttpMethod.Get, cases);
        Assert.True(list.IsSuccessStatusCode, await list.Content.ReadAsStringAsync());
        var review = (await list.Content.ReadFromJsonAsync<JsonElement>())[0];
        using var decision = await P0212Fixture.PostAsync(admin, $"{cases}/{review.GetProperty("caseId").GetGuid():D}/decisions",
            new ReviewPaymentCommand(Guid.NewGuid(), 0, "ReferForRefundReview", "Review held payment.", "FIXTURE-879"));
        Assert.True(decision.StatusCode == HttpStatusCode.Created, await decision.Content.ReadAsStringAsync());
        await ReconcileAsync(fixture, rig.Payer.ShopId);
        Assert.Equal("Security", (await fixture.LatestSnapshotAsync(rig.Payer.ShopId)).Account!.Restriction);
        Assert.Equal(0L, await rig.Fixture.CountAsync("operational_restriction_releases"));
    }

    private static async Task AssertStillSuspendedAsync(P0213Fixture fixture, Guid shop)
    {
        var snapshot = await fixture.LatestSnapshotAsync(shop);
        Assert.Equal("Security", snapshot.Shop!.Suspension?.Mode);
        Assert.Null(snapshot.Shop.ReleasedRevision);
        Assert.Equal(0L, await fixture.Trial.CountAsync("operational_restriction_releases"));
    }

    private static async Task ReconcileAsync(P0213Fixture fixture, Guid shop)
    {
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            UPDATE entitlement_shop_heads SET revision = revision + 1,
                next_verification_at = timestamptz '2000-01-01T00:00:00Z'
            WHERE shop_id = '{shop:D}'
            """);
        await fixture.ScanAsync(source: true);
    }
}
