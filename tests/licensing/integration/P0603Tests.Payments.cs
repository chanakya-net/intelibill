using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0603Tests
{
    [Fact]
    public async Task EarlyFullPaymentWaitsForRenewalAndFencesOrdinaryRenewals()
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");

        var solo = await PublishReductionPlanAsync(fixture, "single-shop", "Solo Early", 1, 3, 6000L);
        var cheaperSameSlots = await PublishReductionPlanAsync(fixture, "multi-shop", "Cheaper Same Slots", 3, 3, 9500L);
        fixture.At(February14);

        var preview = await CreatePreviewAsync(package, term, solo, 1, 3, 5, February29);
        var schedule = await ScheduleAsync(package, term, preview, [package.Shops[0].ShopId]);

        // Scheduling fences ordinary renewals (both standard renewal and cheaper seat allowance at same slot count).
        using var ordinaryRenewal = await package.Payer.Client.PostAsJsonAsync($"/api/billing/accounts/{package.Account:D}/orders",
            new { operationKey = Guid.NewGuid(), planVersionId = package.Plan, period = "Month", shopIds = Array.Empty<Guid>(),
                extraShops = 0, expectedRevision = 1, termId = term });
        Assert.Equal(HttpStatusCode.Conflict, ordinaryRenewal.StatusCode);
        Assert.Equal(CheckoutOrdersV1.SourceChanged, await P0206Fixture.CodeAsync(ordinaryRenewal));

        using var sameSlotLowerSeat = await package.Payer.Client.PostAsJsonAsync($"/api/billing/accounts/{package.Account:D}/orders",
            new { operationKey = Guid.NewGuid(), planVersionId = cheaperSameSlots, period = "Month", shopIds = Array.Empty<Guid>(),
                extraShops = 0, expectedRevision = 1, termId = term });
        Assert.Equal(HttpStatusCode.Conflict, sameSlotLowerSeat.StatusCode);

        // Manager and foreign owner are denied on order creation before lookup/replay.
        var manager = package.Shops[1];
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            INSERT INTO billing_account_members(id, account_id, merchant_user_id, role, status, revision, created_at)
            VALUES (gen_random_uuid(), '{package.Account:D}', '{manager.UserId:D}', 2, 1, 1, now())
            """);
        using var managerOrder = await manager.Client.PostAsJsonAsync(
            $"{ReductionsPath(package.Account, term)}/{schedule.ScheduleId:D}/order",
            new CreateReductionOrderV1(Guid.NewGuid(), schedule.SnapshotHash));
        Assert.Equal(HttpStatusCode.Forbidden, managerOrder.StatusCode);

        // Owner creates explicit checkout order from frozen schedule, accepts, and pays early via simulator.
        var orderKey = Guid.NewGuid();
        var order = await CreateReductionOrderAsync(package, term, schedule, orderKey);
        Assert.Equal(6000L, order.AmountPaise);
        Assert.Equal(1, order.ShopCount);
        Assert.Equal(February29, order.PreviewStartsAt);
        Assert.Equal(schedule.ScheduleId, order.Reduction?.ScheduleId);
        // Replay returns the same order.
        Assert.Equal(order.OrderId, (await CreateReductionOrderAsync(package, term, schedule, orderKey)).OrderId);

        // Manager cannot start payment on a reduction order.
        var accepted = await AcceptAsync(package, order);
        using var managerStart = await manager.Client.PostAsJsonAsync(P0302Fixture.AttemptsPath(accepted), P0302Fixture.Start(accepted));
        Assert.Equal(HttpStatusCode.Forbidden, managerStart.StatusCode);

        // Browser-only unverified return creates no funding or successor.
        var attempt = await StartPaymentAsync(package, accepted);
        var unverified = await P0302Fixture.VerifyAsync(package.Payer.Client, accepted, attempt);
        Assert.Equal("AwaitingPayment", unverified.GetProperty("payment").GetString());
        Assert.Equal("Scheduled", (await ReadScheduleAsync(package, term)).State);
        Assert.Equal(1L, await fixture.CountAsync("subscription_coverage_terms"));

        // Full simulator capture funds the reduction without changing current access before February 29.
        await P0302Fixture.CaptureAsync(package.Payer.Client, accepted, attempt);
        var paid = await P0302Fixture.VerifyAsync(package.Payer.Client, accepted, attempt);
        P0302Fixture.AssertStatus(paid, "Verified", "Pending", "NotApplicable");
        var funded = await ReadScheduleAsync(package, term);
        Assert.Equal("Funded", funded.State);
        Assert.Equal(accepted.OrderId, funded.OrderId);
        Assert.Null(funded.AppliedTermId);
        Assert.Equal(1L, await fixture.CountAsync("payment_confirmations", $"order_id = '{accepted.OrderId:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("subscription_coverage_terms"));

        // Running due scan BEFORE February 29 does not apply early.
        await DrainReductionAsync(fixture.Checkout.Delivery);
        Assert.Equal("Funded", (await ReadScheduleAsync(package, term)).State);
        Assert.Equal(1L, await fixture.CountAsync("subscription_coverage_terms"));
        var liveTerm = await AccountTermAsync(package, term);
        Assert.Equal(3, liveTerm.GetProperty("purchasedCapacity").GetInt32());
        Assert.Equal(5, liveTerm.GetProperty("memberAllowance").GetInt32());
    }

    [Fact]
    public async Task EarlyManualReceiptFundsReductionAndStalePaidOrderGoesToFinanceReview()
    {
        await using var fixture = await StartAsync(database, January31, P0306Tests.Settings);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");
        var solo = await PublishReductionPlanAsync(fixture, "single-shop", "Solo Manual", 1, 3, 6000L);
        fixture.At(February14);

        var preview = await CreatePreviewAsync(package, term, solo, 1, 3, 5, February29);
        var schedule = await ScheduleAsync(package, term, preview, [package.Shops[0].ShopId]);
        var order = await AcceptAsync(package, await CreateReductionOrderAsync(package, term, schedule));

        var billingSeed = await P0306Tests.EnrollBillingAsync(fixture.Checkout, P0306Tests.BillingEmail);
        using var finance = await fixture.Checkout.Trial.Catalogue.SignInOperatorAsync(billingSeed, P0306Tests.BillingEmail);
        var receiptsPath = P0306Tests.Path(order);
        var receipt = P0306Tests.Receipt(order, February14);

        // Preview exposes ReductionPending reason; wrong amount is rejected.
        var badAmount = P0306Tests.Receipt(order, February14) with { AmountPaise = order.AmountPaise - 1 };
        using var rejected = await P0212Fixture.PostAsync(finance, receiptsPath, badAmount);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        using var previewed = await P0212Fixture.PostAsync(finance, receiptsPath + "/preview", receipt);
        previewed.EnsureSuccessStatusCode();
        Assert.Equal("ReductionPending", (await previewed.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("reviewReason").GetString());

        using var confirmed = await P0212Fixture.PostAsync(finance, receiptsPath, receipt);
        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
        Assert.Equal("Pending", (await confirmed.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("fulfilment").GetString());
        Assert.Equal("Funded", (await ReadScheduleAsync(package, term)).State);
        Assert.Equal(1L, await fixture.CountAsync("payment_confirmations", $"order_id = '{order.OrderId:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("subscription_coverage_terms"));

        // Now suspend Shop[1] BEFORE renewal so the source changes -> at boundary, funded schedule goes to NeedsReview
        // preserving payment evidence without applying another plan, renewing prior, or selecting another shop.
        using var security = await P0417Tests.SignInAsync(fixture.Checkout.Delivery, "security-administrator");
        await P0404Tests.SuspendAsync(security, $"/api/admin/accounts/{package.Account:D}/restrictions",
            "Shop", package.Shops[1].ShopId, "Trading");
        fixture.At(February29);
        await DrainReductionAsync(fixture.Checkout.Delivery);

        var reviewed = await ReadScheduleAsync(package, term);
        Assert.Equal("NeedsReview", reviewed.State);
        Assert.Equal("SourceChanged", reviewed.Reason);
        Assert.Equal(1L, await fixture.CountAsync("subscription_coverage_terms"));
        using var financeAtBoundary = await fixture.Checkout.Trial.Catalogue.SignInOperatorAsync(billingSeed, P0306Tests.BillingEmail);
        using var reviews = await financeAtBoundary.SendAsync(HttpMethod.Get, $"/api/admin/accounts/{package.Account:D}/payment-reviews");
        reviews.EnsureSuccessStatusCode();
        var reviewCase = Assert.Single((await reviews.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
        Assert.Equal("SourceChanged", reviewCase.GetProperty("classification").GetString());
        Assert.Equal(order.OrderId, reviewCase.GetProperty("orderId").GetGuid());
    }
}
