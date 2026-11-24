using System.Net;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0609Tests
{
    // A property: static initializers across partial files have no defined order.
    private static DateTimeOffset Boundary => Instant.AddDays(10);

    [Theory]
    [InlineData("Preserve")]
    [InlineData("EndAt")]
    [InlineData("TermAdjustment")]
    public async Task ExplicitAccessTreatmentAffectsOnlyTheNamedTerm(string treatment)
    {
        await using var fixture = await StartAsync(database, Instant);
        var (package, named, independent, request) = await TwoSubscriptionsAsync(fixture);
        var original = await TermAsync(named.Shops[0]);
        var paidThrough = original.GetProperty("paidThrough").GetDateTimeOffset();
        var accessEnds = original.GetProperty("accessEndsAt").GetDateTimeOffset();
        var untouched = await UnrelatedFactsAsync(fixture, independent.Shops[0]);
        var snapshots = await fixture.CountAsync("entitlement_snapshots");
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        DateTimeOffset? at = treatment == "Preserve" ? null : Boundary;

        // The preview names exactly the two shops of the funded term and the exact instants before and after.
        var preview = await PreviewAsync(billing, package.Account, request, 4500L, treatment, at);
        var (newPaid, newEnds) = treatment switch
        {
            "EndAt" => (Boundary, Boundary),
            "TermAdjustment" => (Boundary, Boundary.AddDays(7)),
            _ => (paidThrough, accessEnds),
        };
        AssertImpact(preview.GetProperty("access"), named, (paidThrough, accessEnds), (newPaid, newEnds));

        var receipt = await ReadAsync(await ApproveAsync(billing, package.Account, request,
            Command(preview, 4500L, treatment, at)), HttpStatusCode.Created);
        Assert.Equal(treatment, receipt.GetProperty("access").GetProperty("treatment").GetString());
        await P0608Tests.DrainAsync(fixture);

        // Only an effective treatment publishes, and only for the two named shops.
        Assert.Equal(snapshots + (treatment == "Preserve" ? 0 : 2), await fixture.CountAsync("entitlement_snapshots"));
        Assert.Equal(1L, await fixture.CountAsync("refund_access_decisions",
            $"treatment = '{treatment}' AND shop_count = 2 AND term_revision IS {(treatment == "Preserve" ? "" : "NOT ")}NULL"));

        // Just before the reviewed instant both shops still trade; exactly at it the explicit effect applies.
        fixture.At(Boundary.AddTicks(-10));
        foreach (var shop in named.Shops)
            Assert.Equal("Active", (await TermAsync(shop)).GetProperty("state").GetString());
        fixture.At(Boundary);
        foreach (var shop in named.Shops)
        {
            var term = await TermAsync(shop);
            Assert.Equal(treatment switch { "EndAt" => "Expired", "TermAdjustment" => "Grace", _ => "Active" },
                term.GetProperty("state").GetString());
            Assert.Equal(newPaid, term.GetProperty("paidThrough").GetDateTimeOffset());
            Assert.Equal(newEnds, term.GetProperty("accessEndsAt").GetDateTimeOffset());
        }
        if (treatment == "TermAdjustment")
        {
            fixture.At(Boundary.AddDays(7));
            Assert.Equal("Expired", (await TermAsync(named.Shops[0])).GetProperty("state").GetString());
            fixture.At(Boundary);
        }

        // The independent subscription, the purchased calendar and every membership, restriction and money fact stay put.
        Assert.Equal(untouched, await UnrelatedFactsAsync(fixture, independent.Shops[0]));
        Assert.Equal("Active", (await TermAsync(independent.Shops[0])).GetProperty("state").GetString());
        await RecordAsync("access-" + treatment, new
        {
            treatment, fixedClock = Instant, reviewedInstant = at, namedShops = 2, previousPaidThrough = paidThrough,
            previousAccessEndsAt = accessEnds, newPaidThrough = newPaid, newAccessEndsAt = newEnds,
            stateJustBefore = "Active", stateAtInstant = treatment switch { "EndAt" => "Expired", "TermAdjustment" => "Grace", _ => "Active" },
            snapshotsPublished = treatment == "Preserve" ? 0 : 2, independentSubscriptionUnchanged = true,
            calendarMembersRestrictionsAndMoneyUnchanged = true,
        });
    }

    [Fact]
    public async Task EffectiveTreatmentIsRefusedOutsideTheCurrentUncommittedTerm()
    {
        await using var fixture = await StartAsync(database, Instant);
        var package = await fixture.PackageAsync(1, ["Renewed Shop"]);
        var (purchase, term) = await PurchaseAsync(package, "Month");
        var confirmation = await P0608Tests.ConfirmationIdAsync(package.Payer.Client, package.Account, purchase.OrderId);
        var request = await RequestRefundAsync(package, purchase.OrderId, confirmation, 6000L);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var paidThrough = (await TermAsync(package.Shops[0])).GetProperty("paidThrough").GetDateTimeOffset();

        // Instants outside the term's remaining access, or not at storage precision, are refused.
        foreach (var (treatment, at) in new (string, DateTimeOffset)[]
        {
            ("EndAt", Instant), ("EndAt", paidThrough.AddDays(7).AddTicks(10)), ("EndAt", Boundary.AddTicks(1)),
            ("TermAdjustment", Instant), ("TermAdjustment", paidThrough),
        })
            await AssertCodeAsync(await TryPreviewAsync(billing, package.Account, request, 1000L, treatment, at),
                HttpStatusCode.Conflict, "Refund.AccessTreatmentUnavailable");
        // A treatment without its instant, or Preserve with one, is not a decision at all.
        await AssertCodeAsync(await TryPreviewAsync(billing, package.Account, request, 1000L, "EndAt"),
            HttpStatusCode.BadRequest, "Refund.ApprovalInvalid");
        await AssertCodeAsync(await TryPreviewAsync(billing, package.Account, request, 1000L, "Preserve", Boundary),
            HttpStatusCode.BadRequest, "Refund.ApprovalInvalid");
        await AssertCodeAsync(await TryPreviewAsync(billing, package.Account, request, 1000L, "Suspend"),
            HttpStatusCode.BadRequest, "Refund.ApprovalInvalid");

        // A reviewed EndAt goes stale when the term is renewed before it is confirmed: the commitment wins.
        var reviewed = await PreviewAsync(billing, package.Account, request, 1000L, "EndAt", Boundary);
        await RenewAsync(package, term, "Month");
        await AssertCodeAsync(await ApproveAsync(billing, package.Account, request, Command(reviewed, 1000L, "EndAt", Boundary)),
            HttpStatusCode.Conflict, "Refund.PreviewStale");
        await AssertCodeAsync(await TryPreviewAsync(billing, package.Account, request, 1000L, "EndAt", Boundary),
            HttpStatusCode.Conflict, "Refund.AccessTreatmentUnavailable");
        Assert.Equal(0L, await fixture.CountAsync("refund_approvals"));

        // Preserve remains available and writes no access fact.
        var preserve = await PreviewAsync(billing, package.Account, request, 1000L, "Preserve");
        await ReadAsync(await ApproveAsync(billing, package.Account, request, Command(preserve, 1000L, "Preserve")),
            HttpStatusCode.Created);
        Assert.Equal(0L, await fixture.CountAsync("refund_access_decisions", "treatment <> 'Preserve'"));
    }

    [Fact]
    public async Task EndedTermStopsRenewalAndLaterDecisionsOnlyMoveEarlier()
    {
        await using var fixture = await StartAsync(database, Instant);
        var (package, first, confirmation, order) = await RequestedAsync(fixture, 3000L);
        var second = await RequestRefundAsync(package, order, confirmation, 3000L);
        var term = (await TermAsync(package.Shops[0])).GetProperty("termId").GetGuid();
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);

        var adjust = await PreviewAsync(billing, package.Account, first, 3000L, "TermAdjustment", Boundary);
        var end = await PreviewAsync(billing, package.Account, second, 3000L, "EndAt", Boundary.AddDays(3));
        await ReadAsync(await ApproveAsync(billing, package.Account, first,
            Command(adjust, 3000L, "TermAdjustment", Boundary)), HttpStatusCode.Created);

        // The second reviewed effect was judged against the earlier access: it must be reviewed again.
        await AssertCodeAsync(await ApproveAsync(billing, package.Account, second,
            Command(end, 3000L, "EndAt", Boundary.AddDays(3))), HttpStatusCode.Conflict, "Refund.PreviewStale");
        end = await PreviewAsync(billing, package.Account, second, 3000L, "EndAt", Boundary.AddDays(3));
        Assert.Equal(Boundary, end.GetProperty("access").GetProperty("previousPaidThrough").GetDateTimeOffset());
        Assert.Equal(Boundary.AddDays(3), end.GetProperty("access").GetProperty("newAccessEndsAt").GetDateTimeOffset());
        await ReadAsync(await ApproveAsync(billing, package.Account, second,
            Command(end, 3000L, "EndAt", Boundary.AddDays(3))), HttpStatusCode.Created);
        Assert.Equal(2L, await fixture.CountAsync("refund_access_decisions", $"term_id = '{term:D}' AND term_revision IN (1, 2)"));

        // The original calendar row is untouched; a decided term is no longer a renewal source.
        var status = await TermAsync(package.Shops[0]);
        Assert.Equal(Boundary, status.GetProperty("paidThrough").GetDateTimeOffset());
        Assert.Equal(Boundary.AddDays(3), status.GetProperty("accessEndsAt").GetDateTimeOffset());
        Assert.Equal(1L, await fixture.CountAsync("subscription_coverage_terms", $"term_id = '{term:D}' AND ends_at > '{Boundary.AddDays(3):O}'"));
        using var renewal = await TryRenewalQuoteAsync(package, term, "Month");
        Assert.False(renewal.IsSuccessStatusCode);
    }

    private static void AssertImpact(JsonElement access, Package named, (DateTimeOffset Paid, DateTimeOffset Ends) before,
        (DateTimeOffset Paid, DateTimeOffset Ends) after)
    {
        Assert.Equal(named.Shops.Select(shop => shop.ShopId).Order().ToArray(),
            access.GetProperty("shops").EnumerateArray().Select(shop => shop.GetProperty("shopId").GetGuid()).Order().ToArray());
        Assert.Equal(before.Paid, access.GetProperty("previousPaidThrough").GetDateTimeOffset());
        Assert.Equal(before.Ends, access.GetProperty("previousAccessEndsAt").GetDateTimeOffset());
        Assert.Equal(after.Paid, access.GetProperty("newPaidThrough").GetDateTimeOffset());
        Assert.Equal(after.Ends, access.GetProperty("newAccessEndsAt").GetDateTimeOffset());
    }

    /// <summary>A two-shop paid term with one refund request, plus an independent one-shop subscription.</summary>
    private static async Task<(Package Package, Package Named, Package Independent, Guid Request)> TwoSubscriptionsAsync(
        P0308Fixture fixture)
    {
        var package = await fixture.PackageAsync(3, ["Named North", "Named South", "Independent"]);
        var named = package with { Shops = package.Shops[..2] };
        var independent = package with { Shops = package.Shops[2..] };
        var (purchase, _) = await PurchaseAsync(named, "Month");
        await PurchaseAsync(independent, "Month");
        await P0608Tests.DrainAsync(fixture);
        var confirmation = await P0608Tests.ConfirmationIdAsync(package.Payer.Client, package.Account, purchase.OrderId);
        return (package, named, independent, await RequestRefundAsync(package, purchase.OrderId, confirmation, 6000L));
    }

    private static async Task<JsonElement> TermAsync(P0209Fixture.Merchant shop) =>
        (await P0209Fixture.ReadAsync(await P0209Fixture.ShopStatusAsync(shop.Client, shop.ShopId))).GetProperty("term");

    // Facts no refund decision may touch: the other subscription, calendars, money, members, restrictions, closures.
    private static async Task<string> UnrelatedFactsAsync(P0308Fixture fixture, P0209Fixture.Merchant independent) =>
        string.Join('|',
            (await TermAsync(independent)).GetRawText(),
            await fixture.ScalarAsync("SELECT string_agg(term_id::text || starts_at::text || ends_at::text || capacity::text, ',' ORDER BY term_id) FROM public.subscription_coverage_terms"),
            await fixture.ScalarAsync("SELECT string_agg(term_id::text || price_paise::text || anchor_local::text, ',' ORDER BY term_id) FROM public.subscription_term_facts"),
            await fixture.ScalarAsync("SELECT string_agg(shop_id::text || coalesce(term_id::text, '') || request_id::text, ',' ORDER BY shop_id) FROM public.shop_affiliations"),
            await fixture.ScalarAsync("SELECT string_agg(merchant_user_id::text || role::text || status::text, ',' ORDER BY merchant_user_id) FROM public.billing_account_members"),
            await fixture.CountAsync("payment_fulfilments"), await fixture.CountAsync("verified_payments"),
            await fixture.CountAsync("payment_confirmations"), await fixture.CountAsync("payment_events"),
            await fixture.CountAsync("operational_restrictions"), await fixture.CountAsync("operational_closures"),
            await fixture.CountAsync("refund_requests"));
}
