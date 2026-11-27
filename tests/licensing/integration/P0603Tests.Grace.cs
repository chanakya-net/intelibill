using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0603Tests
{
    [Fact]
    public async Task PendingReductionPreservesOnlyTheOriginalGraceWindowAcrossAllFourBoundaryInstants()
    {
        await using var fixture = await StartAsync(database, January31);
        var control = await fixture.PackageAsync(2, ["Control A", "Control B"]);
        var unpaid = await fixture.PackageAsync(2, ["Unpaid A", "Unpaid B"]);
        var conflicted = await fixture.PackageAsync(2, ["Conflict A", "Conflict B"]);

        var (_, controlTerm) = await PurchaseAsync(control, "Month");
        var (_, unpaidTerm) = await PurchaseAsync(unpaid, "Month");
        var (_, conflictTerm) = await PurchaseAsync(conflicted, "Month");
        var delivery = fixture.Checkout.Delivery;
        await delivery.ScanAsync(true);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);

        var solo = await PublishReductionPlanAsync(fixture, "single-shop", "Solo Grace", 1, 2, 6000L);
        fixture.At(February14);

        // Schedule unpaid reduction on `unpaid`.
        var unpaidPreview = await CreatePreviewAsync(unpaid, unpaidTerm, solo, 1, 3, 5, February29);
        await ScheduleAsync(unpaid, unpaidTerm, unpaidPreview, [unpaid.Shops[0].ShopId]);

        // Schedule AND fund reduction on `conflicted`, then add staff so it blocks into NeedsResolution at paidThrough.
        var conflictPreview = await CreatePreviewAsync(conflicted, conflictTerm, solo, 1, 3, 5, February29);
        var conflictSchedule = await ScheduleAsync(conflicted, conflictTerm, conflictPreview, [conflicted.Shops[0].ShopId]);
        var conflictOrder = await AcceptAsync(conflicted, await CreateReductionOrderAsync(conflicted, conflictTerm, conflictSchedule));
        await PayAsync(conflicted, conflictOrder);
        for (var i = 0; i < 2; i++)
            await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Staff, conflicted.Shops[0].ShopId);

        var graceEnd = February29.AddDays(7);
        var instants = new[]
        {
            (At: February29.AddTicks(-10), State: "Active"),
            (At: February29, State: "Grace"),
            (At: graceEnd.AddTicks(-10), State: "Grace"),
            (At: graceEnd, State: "Expired"),
        };

        foreach (var (at, expected) in instants)
        {
            fixture.At(at);
            if (at == February29)
            {
                await DrainReductionAsync(fixture.Checkout.Delivery, rounds: 3);
                Assert.Equal("NeedsResolution", (await ReadScheduleAsync(conflicted, conflictTerm)).State);
            }
            var cStatus = await AccountTermAsync(control, controlTerm);
            var uStatus = await AccountTermAsync(unpaid, unpaidTerm);
            var fStatus = await AccountTermAsync(conflicted, conflictTerm);
            Assert.Equal(expected, cStatus.GetProperty("state").GetString());
            Assert.Equal(expected, uStatus.GetProperty("state").GetString());
            Assert.Equal(expected, fStatus.GetProperty("state").GetString());
            Assert.Equal(cStatus.GetProperty("graceEndsAt").GetDateTimeOffset(), uStatus.GetProperty("graceEndsAt").GetDateTimeOffset());
            Assert.Equal(cStatus.GetProperty("graceEndsAt").GetDateTimeOffset(), fStatus.GetProperty("graceEndsAt").GetDateTimeOffset());
            Assert.Equal(cStatus.GetProperty("accessEndsAt").GetDateTimeOffset(), uStatus.GetProperty("accessEndsAt").GetDateTimeOffset());
            Assert.Equal(cStatus.GetProperty("accessEndsAt").GetDateTimeOffset(), fStatus.GetProperty("accessEndsAt").GetDateTimeOffset());
        }
    }

    [Fact]
    public async Task InWindowCaptureVerifiedLateAppliesAndOmittedShopHasNoGraceUnlessGrantedCourtesy()
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");
        var delivery = fixture.Checkout.Delivery;
        await delivery.ScanAsync(true);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);

        var solo = await PublishReductionPlanAsync(fixture, "single-shop", "Solo LateVerify", 1, 3, 6000L);
        fixture.At(February14);
        var preview = await CreatePreviewAsync(package, term, solo, 1, 3, 5, February29);
        var schedule = await ScheduleAsync(package, term, preview, [package.Shops[0].ShopId]);

        // Create & accept the reduction order inside grace, start & capture at graceEnd - 1us, then verify LATE at graceEnd + 1 hour!
        var graceEnd = February29.AddDays(7);
        fixture.At(graceEnd.AddMinutes(-5));
        var order = await AcceptAsync(package, await CreateReductionOrderAsync(package, term, schedule));
        fixture.At(graceEnd.AddTicks(-10));
        var attempt = await StartPaymentAsync(package, order);
        await P0302Fixture.CaptureAsync(package.Payer.Client, order, attempt);

        fixture.At(graceEnd.AddHours(1));
        await delivery.ScanAsync(true);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);

        var verified = await P0302Fixture.VerifyAsync(package.Payer.Client, order, attempt);
        P0302Fixture.AssertStatus(verified, "Verified", "Pending", "NotApplicable");
        await DrainReductionAsync(delivery, rounds: 4);

        var applied = await ReadScheduleAsync(package, term);
        Assert.Equal("Applied", applied.State);

        // Omitted shop has NO renewed predecessor grace -> Expired with graceEndsAt = null.
        using var omittedStatus = await package.Shops[1].Client.GetAsync($"/api/licensing/shops/{package.Shops[1].ShopId:D}/status");
        omittedStatus.EnsureSuccessStatusCode();
        var omittedJson = await omittedStatus.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Expired", omittedJson.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, omittedJson.GetProperty("term").GetProperty("graceEndsAt").ValueKind);

        // An independent complimentary grant on the omitted shop is still honored cleanly via the real admin API.
        var grantStart = graceEnd.AddHours(1);
        var grantEnd = grantStart.AddDays(10);
        using var licensingManager = await P0417Tests.SignInAsync(delivery, "licensing-manager");
        await P0212Fixture.GrantAsync(licensingManager, package.Account,
            P0212Fixture.Draft([package.Shops[1].ShopId], grantStart, grantEnd, memberAllowance: 3,
                reason: "Omitted shop transition support"));
        using var courtesyStatus = await package.Shops[1].Client.GetAsync($"/api/licensing/shops/{package.Shops[1].ShopId:D}/status");
        courtesyStatus.EnsureSuccessStatusCode();
        var courtesyJson = await courtesyStatus.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Active", courtesyJson.GetProperty("state").GetString());
        Assert.Equal("Complimentary", courtesyJson.GetProperty("term").GetProperty("basis").GetString());
    }

    [Fact]
    public async Task PaymentCapturedAfterGraceWindowEntersFinanceReviewWithoutApplyingReduction()
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");
        var solo = await PublishReductionPlanAsync(fixture, "single-shop", "Solo OutOfWindow", 1, 3, 6000L);
        fixture.At(February14);

        var preview = await CreatePreviewAsync(package, term, solo, 1, 3, 5, February29);
        var schedule = await ScheduleAsync(package, term, preview, [package.Shops[0].ShopId]);

        // Create, accept & start inside grace window, but capture AFTER graceEnd!
        var graceEnd = February29.AddDays(7);
        fixture.At(graceEnd.AddMinutes(-5));
        var order = await AcceptAsync(package, await CreateReductionOrderAsync(package, term, schedule));
        var attempt = await StartPaymentAsync(package, order);

        fixture.At(graceEnd.AddMinutes(1));
        await P0302Fixture.CaptureAsync(package.Payer.Client, order, attempt);
        var verified = await P0302Fixture.VerifyAsync(package.Payer.Client, order, attempt);
        P0302Fixture.AssertStatus(verified, "Verified", "NeedsReview", "NotApplicable");
        Assert.Equal("Scheduled", (await ReadScheduleAsync(package, term)).State);
        Assert.Equal(1L, await fixture.CountAsync("subscription_coverage_terms"));
        Assert.Equal(1L, await fixture.CountAsync("unmatched_payments", $"order_id = '{order.OrderId:D}' AND classification = 'TermUnavailable'"));

        // An unfailed payment attempt on the Scheduled reduction order still blocks post-grace RestoredAccess.
        using var blockedAfterCapture = await TryRenewalQuoteAsync(package, term, "Month");
        Assert.Equal(HttpStatusCode.Conflict, blockedAfterCapture.StatusCode);
        Assert.Equal(CheckoutOrdersV1.SourceChanged, await P0206Fixture.CodeAsync(blockedAfterCapture));
    }

    [Fact]
    public async Task CancelledTermIsGraceIneligibleAndCannotScheduleReduction()
    {
        await using var fixture = await StartAsync(database, February14);
        var payer = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        var second = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await P0209Fixture.CreateAccountAsync(payer.Client);
        var term = await P0209Fixture.SeedTermAsync(fixture.Hosts, account, new(January31, February29,
            AnchorLocal: "2028-01-31 10:00:00", Grouped: true, Capacity: 3, MemberAllowance: 5, PricePaise: 12000L));
        foreach (var shop in new[] { payer, second })
            await P0209Fixture.AffiliateAsync(fixture.Hosts, account, payer.UserId, shop.ShopId, shop.UserId,
                term, January31, February29);
        var package = new Package(payer, account, [payer, second], Guid.Empty);
        var solo = await PublishReductionPlanAsync(fixture, "single-shop", "Solo Cancelled", 1, 3, 6000L);

        // Schedule end-of-period cancellation on the paid term using its live preview facts.
        var cancelPath = P0215Tests.CancellationPath(account, term);
        var detail = await P0215Fixture.PreviewAsync(payer, cancelPath);
        using var cancelled = await payer.Client.PostAsJsonAsync(cancelPath, P0215Tests.Accept(detail, Guid.NewGuid()));
        Assert.True(cancelled.IsSuccessStatusCode, await cancelled.Content.ReadAsStringAsync());

        // Cancelled term cannot preview a reduction and has no grace at paidThrough.
        using var preview = await PostPreviewAsync(package, term, solo, 1, 3, 5, February29);
        Assert.Equal(HttpStatusCode.Conflict, preview.StatusCode);
        Assert.Equal(CapacityReductionsV1.SourceChanged, await P0206Fixture.CodeAsync(preview));

        fixture.At(February29);
        var atBoundary = await AccountTermAsync(package, term);
        Assert.Equal("Expired", atBoundary.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, atBoundary.GetProperty("graceEndsAt").ValueKind);
    }

    [Fact]
    public async Task PostGraceRestoredAccessSucceedsAfterUnpaidScheduledReductionExpires()
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");
        var solo = await PublishReductionPlanAsync(fixture, "single-shop", "Solo Unpaid Expired", 1, 3, 6000L);
        fixture.At(February14);

        var preview = await CreatePreviewAsync(package, term, solo, 1, 3, 5, February29);
        var schedule = await ScheduleAsync(package, term, preview, [package.Shops[0].ShopId]);

        // Inside the grace window, ordinary PriorPaidThrough renewal remains fenced by the scheduled reduction.
        var graceEnd = February29.AddDays(7);
        fixture.At(graceEnd.AddTicks(-10));
        using var inGraceBlocked = await TryRenewalQuoteAsync(package, term, "Month");
        Assert.Equal(HttpStatusCode.Conflict, inGraceBlocked.StatusCode);
        Assert.Equal(CheckoutOrdersV1.SourceChanged, await P0206Fixture.CodeAsync(inGraceBlocked));

        // After graceEnd, reduction orders are closed and ordinary RestoredAccess purchase succeeds.
        fixture.At(graceEnd);
        using var closedReductionOrder = await package.Payer.Client.PostAsJsonAsync(
            $"{ReductionsPath(package.Account, term)}/{schedule.ScheduleId:D}/order",
            new CreateReductionOrderV1(Guid.NewGuid(), schedule.SnapshotHash));
        Assert.Equal(HttpStatusCode.Conflict, closedReductionOrder.StatusCode);

        var restoredQuote = await AcceptAsync(package, await RenewalQuoteAsync(package, term, "Month"));
        Assert.Equal("RestoredAccess", restoredQuote.StartRule);
        Assert.Equal(0, restoredQuote.GraceDaysIncluded);
        Assert.Equal(2, restoredQuote.ShopCount);

        var paid = await PayAsync(package, restoredQuote);
        P0302Fixture.AssertStatus(paid, "Verified", "Fulfilled", "Applying");
        Assert.Equal(graceEnd, paid.GetProperty("termStartsAt").GetDateTimeOffset());
        Assert.Equal(2L, await fixture.CountAsync("subscription_coverage_terms"));
        Assert.Equal(1L, await fixture.CountAsync("paid_fresh_starts",
            $"order_id = '{restoredQuote.OrderId:D}' AND kind = 'RestoredAccess'"));
    }

    [Fact]
    public async Task PostGraceRestoredAccessSucceedsAfterNeedsReviewReductionIsSettledByFinance()
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");
        var delivery = fixture.Checkout.Delivery;
        await delivery.ScanAsync(true);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);

        var solo = await PublishReductionPlanAsync(fixture, "single-shop", "Solo Settled Review", 1, 3, 6000L);
        fixture.At(February14);
        var preview = await CreatePreviewAsync(package, term, solo, 1, 3, 5, February29);
        var schedule = await ScheduleAsync(package, term, preview, [package.Shops[0].ShopId]);
        var redOrder = await AcceptAsync(package, await CreateReductionOrderAsync(package, term, schedule));
        await PayAsync(package, redOrder);

        // Suspend Shops[1] so the funded reduction enters NeedsReview at renewal, then release before post-grace restoration.
        using var security = await P0417Tests.SignInAsync(delivery, "security-administrator");
        var restrictionsPath = $"/api/admin/accounts/{package.Account:D}/restrictions";
        var restrictionId = await P0404Tests.SuspendAsync(security, restrictionsPath, "Shop", package.Shops[1].ShopId, "Trading");
        fixture.At(February29);
        await DrainReductionAsync(delivery);
        Assert.Equal("NeedsReview", (await ReadScheduleAsync(package, term)).State);

        var graceEnd = February29.AddDays(7);
        fixture.At(graceEnd);
        await P0404Tests.SubmitReleaseAsync(security, $"{restrictionsPath}/{restrictionId:D}/release");

        // While the NeedsReview case is unsettled, RestoredAccess remains blocked.
        using var unsettledBlocked = await TryRenewalQuoteAsync(package, term, "Month");
        Assert.Equal(HttpStatusCode.Conflict, unsettledBlocked.StatusCode);
        Assert.Equal(CheckoutOrdersV1.SourceChanged, await P0206Fixture.CodeAsync(unsettledBlocked));

        // Once finance settles the unmatched case with ReferForRefundReview, post-grace RestoredAccess succeeds.
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var reviewsPath = $"/api/admin/accounts/{package.Account:D}/payment-reviews";
        using var reviews = await finance.SendAsync(HttpMethod.Get, reviewsPath);
        reviews.EnsureSuccessStatusCode();
        var reviewCase = Assert.Single((await reviews.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
        var caseId = reviewCase.GetProperty("caseId").GetGuid();
        using var decided = await P0212Fixture.PostAsync(finance, $"{reviewsPath}/{caseId:D}/decisions",
            new ReviewPaymentCommand(Guid.NewGuid(), 0, "ReferForRefundReview", "Settle stale reduction payment", "FIXTURE-911"));
        Assert.Equal(HttpStatusCode.Created, decided.StatusCode);

        var restoredQuote = await AcceptAsync(package, await RenewalQuoteAsync(package, term, "Month"));
        Assert.Equal("RestoredAccess", restoredQuote.StartRule);
        var paid = await PayAsync(package, restoredQuote);
        P0302Fixture.AssertStatus(paid, "Verified", "Fulfilled", "Applying");
        Assert.Equal(2L, await fixture.CountAsync("subscription_coverage_terms"));
        Assert.Equal(1L, await fixture.CountAsync("paid_fresh_starts",
            $"order_id = '{restoredQuote.OrderId:D}' AND kind = 'RestoredAccess'"));
    }
}
