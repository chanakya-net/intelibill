using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0609Tests
{
    [Fact]
    public async Task DuplicateFundsWithoutAFulfilmentSupportOnlyPreserve()
    {
        await using var fixture = await StartAsync(database, P0210Fixture.Start, P0306Tests.Settings);
        var purchase = await fixture.Checkout.AcceptedQuoteAsync(1);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var path = P0306Tests.Path(purchase.Quote);
        using var fulfilled = await P0212Fixture.PostAsync(billing, path, P0306Tests.Receipt(purchase.Quote, P0210Fixture.Start));
        Assert.Equal(HttpStatusCode.Created, fulfilled.StatusCode);
        var funded = await P0608Tests.ConfirmationIdAsync(purchase.Payer.Client, purchase.Account, purchase.Quote.OrderId);
        var duplicate = await ReadAsync(await P0212Fixture.PostAsync(billing, path,
            P0306Tests.Receipt(purchase.Quote, P0210Fixture.Start)), HttpStatusCode.Created);
        Assert.Equal("DuplicateFunds", duplicate.GetProperty("reviewReason").GetString());
        var unfulfilled = (Guid)(await fixture.ScalarAsync(
            $"SELECT confirmation_id FROM public.payment_confirmations WHERE confirmation_id <> '{funded:D}'"))!;
        var package = new Package(purchase.Payer, purchase.Account, purchase.Shops, Guid.Empty);
        var request = await RequestRefundAsync(package, purchase.Quote.OrderId, unfulfilled, 12000L);

        // The duplicate receipt funded no term: there is nothing an effective treatment could name.
        await AssertCodeAsync(await TryPreviewAsync(billing, package.Account, request, 12000L, "EndAt",
            P0210Fixture.Start.AddDays(3)), HttpStatusCode.Conflict, "Refund.AccessTreatmentUnavailable");
        var preview = await PreviewAsync(billing, package.Account, request, 12000L, "Preserve");
        Assert.Equal(JsonValueKind.Null, preview.GetProperty("access").GetProperty("termId").ValueKind);
        Assert.Empty(preview.GetProperty("access").GetProperty("shops").EnumerateArray());
        await ReadAsync(await ApproveAsync(billing, package.Account, request, Command(preview, 12000L, "Preserve")),
            HttpStatusCode.Created);

        // Each receipt keeps its own ledger: the fulfilled payment is still fully free.
        Assert.Equal(1L, await fixture.CountAsync("refund_access_decisions", "term_id IS NULL AND shop_count = 0"));
        var other = await RequestRefundAsync(package, purchase.Quote.OrderId, funded, 12000L);
        Assert.Equal(12000L, (await PreviewAsync(billing, package.Account, other, 12000L, "Preserve"))
            .GetProperty("remainingAmountPaise").GetInt64());
    }

    [Fact]
    public async Task SuspensionKeepsPrecedenceOverAnExplicitEnd()
    {
        await using var fixture = await StartAsync(database, Instant);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000L);
        await P0608Tests.DrainAsync(fixture);
        var shop = package.Shops[0];
        var seed = await P0306Tests.EnrollBillingAsync(fixture.Checkout, P0306Tests.BillingEmail);
        using var security = await P0417Tests.SignInAsync(fixture.Checkout.Delivery, "security-administrator");
        using var billing = await fixture.Checkout.Trial.Catalogue.SignInOperatorAsync(seed, P0306Tests.BillingEmail);

        var reviewed = await PreviewAsync(billing, package.Account, request, 4500L, "EndAt", Boundary);
        Assert.False(Assert.Single(reviewed.GetProperty("access").GetProperty("shops").EnumerateArray())
            .GetProperty("restricted").GetBoolean());
        await P0404Tests.SuspendAsync(security, $"/api/admin/accounts/{package.Account:D}/restrictions", "Shop",
            shop.ShopId, "Trading");

        // The named scope changed after review: the operator must see the restriction before confirming.
        await AssertCodeAsync(await ApproveAsync(billing, package.Account, request, Command(reviewed, 4500L, "EndAt", Boundary)),
            HttpStatusCode.Conflict, "Refund.PreviewStale");
        var current = await PreviewAsync(billing, package.Account, request, 4500L, "EndAt", Boundary);
        Assert.True(Assert.Single(current.GetProperty("access").GetProperty("shops").EnumerateArray())
            .GetProperty("restricted").GetBoolean());
        await ReadAsync(await ApproveAsync(billing, package.Account, request, Command(current, 4500L, "EndAt", Boundary)),
            HttpStatusCode.Created);

        // The refund decision neither releases nor replaces the suspension.
        Assert.Equal(1L, await fixture.CountAsync("operational_restrictions"));
        Assert.Equal(0L, await fixture.CountAsync("operational_restriction_releases"));
        Assert.Equal(1L, await fixture.CountAsync("active_operational_restrictions", $"shop_id = '{shop.ShopId:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_access_decisions", "treatment = 'EndAt'"));
    }

    [Fact]
    public async Task RefundOfAnAppliedCapacityIncreaseNamesItsExistingTerm()
    {
        await using var fixture = await StartAsync(database, Instant);
        var package = await fixture.PackageAsync(2, P0608Tests.ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");
        var successor = await P0601Tests.RepriceAsync(fixture, package, 18000L);
        fixture.At(P0601Tests.February14);
        var quote = await ReadAsync(await P0601Tests.PostQuoteAsync(package, term,
            P0601Tests.Request(successor, 4, 3, P0601Tests.February29)), HttpStatusCode.OK);
        using var converted = await package.Payer.Client.PostAsJsonAsync(
            $"{P0601Tests.QuotesPath(package.Account, term)}/{quote.GetProperty("quoteId").GetGuid():D}/order",
            new { operationKey = Guid.NewGuid(), snapshotHash = quote.GetProperty("snapshotHash").GetString() });
        converted.EnsureSuccessStatusCode();
        var order = await AcceptAsync(package, (await converted.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!);
        var delivery = fixture.Checkout.Delivery;
        await delivery.ScanAsync(true);
        await P0608Tests.DrainAsync(fixture);
        await PayAsync(package, order);
        await P0608Tests.DrainAsync(fixture);
        await P0608Tests.DrainAsync(fixture);
        Assert.Equal(1L, await fixture.CountAsync("capacity_increases", "state = 'Applied'"));
        var confirmation = (Guid)(await fixture.ScalarAsync(
            $"SELECT confirmation_id FROM public.payment_confirmations WHERE order_id = '{order.OrderId:D}'"))!;
        var request = await RequestRefundAsync(package, order.OrderId, confirmation, order.AmountPaise);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var at = P0601Tests.February14.AddDays(5);

        // The increase payment funded the existing term: an explicit end names that term and its current shops.
        var preview = await PreviewAsync(billing, package.Account, request, order.AmountPaise, "EndAt", at);
        Assert.Equal(term, preview.GetProperty("access").GetProperty("termId").GetGuid());
        Assert.Equal(2, preview.GetProperty("access").GetProperty("shops").GetArrayLength());
        Assert.Equal(P0601Tests.February29, preview.GetProperty("access").GetProperty("previousPaidThrough").GetDateTimeOffset());
        await ReadAsync(await ApproveAsync(billing, package.Account, request,
            Command(preview, order.AmountPaise, "EndAt", at)), HttpStatusCode.Created);
        Assert.Equal(1L, await fixture.CountAsync("refund_access_decisions", $"term_id = '{term:D}' AND treatment = 'EndAt'"));
        // The applied capacity and the purchased calendar are retained; only access ends at the reviewed instant.
        Assert.Equal(1L, await fixture.CountAsync("capacity_increases", "state = 'Applied'"));
        Assert.Equal(at, (await P0601Tests.AccountTermAsync(package, term)).GetProperty("accessEndsAt").GetDateTimeOffset());
    }
}
