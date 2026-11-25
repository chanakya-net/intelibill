using System.Net;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0609Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0610Tests
{
    [Theory]
    [InlineData("EndAt", 0)]
    [InlineData("TermAdjustment", 7)]
    public async Task ConfirmationWithSubMicrosecondClockDeliversApprovedBoundaryOnce(string treatment, int graceDays)
    {
        await using var fixture = await P0609Tests.StartAsync(database, Instant);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000);
        await P0608Tests.DrainAsync(fixture);
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var boundary = Instant.AddDays(3);
        var expectedAccess = boundary.AddDays(graceDays);
        var preview = await PreviewAsync(finance, package.Account, request, 4500, treatment, boundary);
        await ReadAsync(await ApproveAsync(finance, package.Account, request, Command(preview, 4500, treatment, boundary)),
            HttpStatusCode.Created);

        var unaligned = Instant.AddMinutes(1).AddTicks(1);
        var normalized = Instant.AddMinutes(1);
        fixture.At(unaligned);
        await PumpRefundsAsync(fixture);
        await P0608Tests.DrainAsync(fixture);

        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Confirmed'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_applications", "disposition='Applied'"));
        Assert.Equal(0L, await fixture.CountAsync("entitlement_outbox", "status IN ('NeedsReview','DeadLetter')"));
        Assert.Equal(normalized.UtcDateTime, (DateTime)(await fixture.ScalarAsync(
            "SELECT observed_at FROM public.refund_observations WHERE outcome='Confirmed'"))!);
        Assert.Equal(normalized.UtcDateTime, (DateTime)(await fixture.ScalarAsync(
            "SELECT observed_at FROM public.refund_applications"))!);

        var delivery = fixture.Checkout.Delivery;
        foreach (var shop in package.Shops)
        {
            var snapshot = await delivery.LatestSnapshotAsync(shop.ShopId);
            Assert.True(snapshot.Shop!.IsValid());
            Assert.Equal(normalized, snapshot.Shop.UpstreamVerifiedAt);
            Assert.Equal(expectedAccess, snapshot.Shop.AccessEndsAt);
            await AssertMerchantDeadlineAndAckAsync(fixture, shop.ShopId, snapshot, expectedAccess);

            // Replay the signed snapshot and drain acknowledgements: deadline, revision and application remain single.
            var (http, ack) = await delivery.SendAsync(snapshot);
            using (http) Assert.True(http.IsSuccessStatusCode, $"{(int)http.StatusCode}");
            Assert.Equal("Applied", ack!.Outcome);
            await P0608Tests.DrainAsync(fixture);
            await AssertMerchantDeadlineAndAckAsync(fixture, shop.ShopId, snapshot, expectedAccess);
        }
        await PumpRefundsAsync(fixture);
        Assert.Equal(1L, await fixture.CountAsync("refund_applications"));
    }

    [Fact]
    public async Task InterveningPaidCapacityIncreaseMovesDelayedRefundAccessToNeedsReview()
    {
        await using var fixture = await P0609Tests.StartAsync(database, Instant);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000);
        await P0608Tests.DrainAsync(fixture);
        var original = await ShopTermAsync(package, package.Shops[0]);
        var term = original.GetProperty("termId").GetGuid();
        var originalPaid = original.GetProperty("paidThrough").GetDateTimeOffset();
        var originalAccess = original.GetProperty("accessEndsAt").GetDateTimeOffset();

        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await ApproveEndAsync(finance, package.Account, request, Instant.AddDays(3));

        await ApplySeatIncreaseAsync(fixture, package, term, originalPaid);
        var snapshotsBefore = await fixture.CountAsync("entitlement_snapshots");

        await PumpRefundsAsync(fixture);
        await P0608Tests.DrainAsync(fixture);
        using var recheck = await P0212Fixture.PostAsync(finance, RequestPath(package.Account, request) + "/reconcile", new { });
        Assert.Equal(HttpStatusCode.OK, recheck.StatusCode);
        await PumpRefundsAsync(fixture);
        await P0608Tests.DrainAsync(fixture);

        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Confirmed'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_sends"));
        Assert.Equal(1L, await fixture.CountAsync("refund_applications", "disposition='NeedsReview'"));
        Assert.Equal(0L, await fixture.CountAsync("refund_applications", "disposition='Applied'"));
        Assert.Equal(snapshotsBefore, await fixture.CountAsync("entitlement_snapshots"));

        var outcome = await MerchantOutcomeAsync(package);
        Assert.Equal("Confirmed", outcome.GetProperty("status").GetString());
        Assert.Equal("NeedsReview", outcome.GetProperty("accessDisposition").GetString());
        foreach (var shop in package.Shops)
        {
            var after = await ShopTermAsync(package, shop);
            Assert.Equal(originalPaid, after.GetProperty("paidThrough").GetDateTimeOffset());
            Assert.Equal(originalAccess, after.GetProperty("accessEndsAt").GetDateTimeOffset());
            Assert.Equal(originalAccess.UtcDateTime, (DateTime)(await fixture.Checkout.Delivery.MerchantScalarAsync(
                $"SELECT access_ends_at FROM public.shop_license_states WHERE shop_id='{shop.ShopId:D}'"))!);
        }
    }

    [Fact]
    public async Task AlreadyAppliedCapacityIncreaseAtApprovalStillPermitsReviewedTreatment()
    {
        await using var fixture = await P0609Tests.StartAsync(database, Instant);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000);
        await P0608Tests.DrainAsync(fixture);
        var original = await ShopTermAsync(package, package.Shops[0]);
        var term = original.GetProperty("termId").GetGuid();
        var originalPaid = original.GetProperty("paidThrough").GetDateTimeOffset();

        await ApplySeatIncreaseAsync(fixture, package, term, originalPaid);
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var boundary = Instant.AddDays(3);
        await ApproveEndAsync(finance, package.Account, request, boundary);

        await PumpRefundsAsync(fixture);
        await P0608Tests.DrainAsync(fixture);

        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Confirmed'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_applications", "disposition='Applied'"));
        foreach (var shop in package.Shops)
        {
            Assert.Equal(boundary, (await ShopTermAsync(package, shop)).GetProperty("accessEndsAt").GetDateTimeOffset());
            Assert.Equal(boundary.UtcDateTime, (DateTime)(await fixture.Checkout.Delivery.MerchantScalarAsync(
                $"SELECT access_ends_at FROM public.shop_license_states WHERE shop_id='{shop.ShopId:D}'"))!);
        }
    }

    private static async Task ApplySeatIncreaseAsync(P0308Fixture fixture, Package package, Guid term, DateTimeOffset end)
    {
        var delivery = fixture.Checkout.Delivery;
        var increase = await P0602Tests.IncreaseSeatsAsync(fixture, package, term, end);
        await delivery.ScanAsync(true);
        await P0608Tests.DrainAsync(fixture);
        await PayAsync(package, increase);
        await P0608Tests.DrainAsync(fixture);
        await P0608Tests.DrainAsync(fixture);
        Assert.Equal(1L, await fixture.CountAsync("capacity_increases", $"term_id='{term:D}' AND state='Applied'"));
    }

    private static async Task AssertMerchantDeadlineAndAckAsync(P0308Fixture fixture, Guid shopId,
        EntitlementSnapshotV1 snapshot, DateTimeOffset expectedAccess)
    {
        var delivery = fixture.Checkout.Delivery;
        Assert.Equal(expectedAccess.UtcDateTime, (DateTime)(await delivery.MerchantScalarAsync(
            $"SELECT access_ends_at FROM public.shop_license_states WHERE shop_id='{shopId:D}'"))!);
        Assert.Equal(snapshot.Shop!.Revision, (long)(await delivery.MerchantScalarAsync(
            $"SELECT revision FROM public.shop_license_states WHERE shop_id='{shopId:D}'"))!);
        Assert.Equal(snapshot.Shop.Revision, (long)(await fixture.ScalarAsync(
            $"SELECT max(shop_revision) FROM public.applied_entitlement_shop_receipts WHERE shop_id='{shopId:D}'"))!);
    }
}
