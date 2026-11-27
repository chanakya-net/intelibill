using System.Net;
using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0308Tests
{
    [Fact]
    public async Task CompetingAndReplayedRenewalPaymentsExtendThePaidTermOnce()
    {
        await using var fixture = await StartAsync(database, March31, P0306Tests.Settings);
        var package = await fixture.PackageAsync(1);
        var (_, first) = await PurchaseAsync(package, "Month");
        fixture.At(Utc("2027-04-20T06:00:00Z"));
        var online = await AcceptAsync(package, await RenewalQuoteAsync(package, first, "Month"));
        var manual = await AcceptAsync(package, await RenewalQuoteAsync(package, first, "Month"));

        var attempt = await StartPaymentAsync(package, online);
        var second = await P0302Fixture.StartAsync(package.Payer.Client, manual, HttpStatusCode.Conflict);
        Assert.Equal(CheckoutPaymentsV1.AlreadyStarted, second.GetProperty("code").GetString());
        await P0302Fixture.CaptureAsync(package.Payer.Client, online, attempt);

        // Finance confirms the other order by bank transfer first; the captured online funds can no longer extend.
        using var billing = await SignInBillingAsync(fixture);
        var confirmed = await ConfirmManualAsync(billing, manual, Utc("2027-04-20T05:00:00Z"));
        Assert.Equal("Fulfilled", confirmed.GetProperty("fulfilment").GetString());
        var lost = await P0302Fixture.VerifyAsync(package.Payer.Client, online, attempt);
        P0302Fixture.AssertStatus(lost, "Verified", "NeedsReview", "NotApplicable");
        Assert.Equal(1L, await fixture.CountAsync("verified_payments", $"order_id = '{online.OrderId:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments", $"predecessor_term_id = '{first:D}'"));
        using (var stale = await TryRenewalQuoteAsync(package, first, "Month"))
            Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);

        // Funds awaiting finance review keep blocking a new online charge for the same shops.
        var renewed = (Guid)(await fixture.ScalarAsync(
            $"SELECT term_id FROM payment_fulfilments WHERE order_id = '{manual.OrderId:D}'"))!;
        var blocked = await AcceptAsync(package, await RenewalQuoteAsync(package, renewed, "Month"));
        Assert.Equal(CheckoutPaymentsV1.AlreadyStarted, (await P0302Fixture.StartAsync(package.Payer.Client, blocked,
            HttpStatusCode.Conflict)).GetProperty("code").GetString());
        Assert.Equal(2L, await fixture.CountAsync("payment_fulfilments", $"account_id = '{package.Account:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("manual_payment_receipts"));
        Assert.Equal(May31, (await ShopTermAsync(package, package.Payer)).GetProperty("paidThrough").GetDateTimeOffset());
    }

    [Fact]
    public async Task ConcurrentVerificationOfOneRenewalOrderAppliesItOnce()
    {
        await using var fixture = await StartAsync(database, March31);
        var package = await fixture.PackageAsync(2);
        var (_, first) = await PurchaseAsync(package, "Month");
        fixture.At(Utc("2027-04-20T06:00:00Z"));
        var quote = await AcceptAsync(package, await RenewalQuoteAsync(package, first, "Month"));
        var attempt = await StartPaymentAsync(package, quote);
        await P0302Fixture.CaptureAsync(package.Payer.Client, quote, attempt);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            P0302Fixture.VerifyAsync(package.Payer.Client, quote, attempt)));
        Assert.Single(results.Where(r => r.TryGetProperty("termId", out var term) && term.ValueKind == JsonValueKind.String)
            .Select(r => r.GetProperty("termId").GetGuid()).Distinct());
        P0302Fixture.AssertStatus(await P0302Fixture.VerifyAsync(package.Payer.Client, quote, attempt),
            "Verified", "Fulfilled", "Applying");
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments", $"predecessor_term_id = '{first:D}'"));
        Assert.Equal(2L, await fixture.CountAsync("payment_fulfilments"));
        Assert.Equal(2L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(4L, await fixture.CountAsync("payment_fulfilment_shops"));
        Assert.Equal(1L, await fixture.ScalarAsync(
            "SELECT count(*) FROM pg_constraint WHERE conname = 'payment_fulfilment_one_successor'"));
        Assert.Equal(May31, (await ShopTermAsync(package, package.Shops[1])).GetProperty("paidThrough").GetDateTimeOffset());
    }

    [Fact]
    public async Task DelayedManualReceiptIsJudgedByItsReceivedTimeAndAcceptedQuote()
    {
        await using var fixture = await StartAsync(database, March31, P0306Tests.Settings);
        var package = await fixture.PackageAsync(1);
        var (_, first) = await PurchaseAsync(package, "Month");
        var receivedAt = April30.AddDays(1).AddHours(-2);
        fixture.At(receivedAt);
        var quote = await AcceptAsync(package, await RenewalQuoteAsync(package, first, "Month"));

        fixture.At(April30.AddDays(9));
        using var billing = await SignInBillingAsync(fixture);
        var confirmed = await ConfirmManualAsync(billing, quote, receivedAt);
        Assert.Equal("Fulfilled", confirmed.GetProperty("fulfilment").GetString());
        Assert.Equal($"{Iso(receivedAt)}|1|renewal/1|{first:D}", await ProvenanceAsync(fixture, quote));
        var status = await P0302Fixture.StatusAsync(package.Payer.Client, quote);
        Assert.Equal(April30, status.GetProperty("termStartsAt").GetDateTimeOffset());
        Assert.Equal(May31, status.GetProperty("termEndsAt").GetDateTimeOffset());
        Assert.Equal("Active", (await ShopTermAsync(package, package.Payer)).GetProperty("state").GetString());
    }

    private static async Task<P0110CookieBrowser> SignInBillingAsync(P0308Fixture fixture)
    {
        var seed = await P0306Tests.EnrollBillingAsync(fixture.Checkout, P0306Tests.BillingEmail);
        return await fixture.Checkout.Trial.Catalogue.SignInOperatorAsync(seed, P0306Tests.BillingEmail);
    }

    private static async Task<JsonElement> ConfirmManualAsync(P0110CookieBrowser billing, CheckoutQuoteV1 quote,
        DateTimeOffset receivedAt)
    {
        var command = new ConfirmManualPayment(Guid.NewGuid(), quote.Revision, quote.SnapshotHash, "fixture-bank", "fixture/1",
            $"BANK-{Guid.NewGuid():N}", "STATEMENT-LOCAL-TEST", receivedAt, quote.AmountPaise, quote.Currency,
            "Reconciled renewal against local fixture statement", true);
        using var preview = await P0212Fixture.PostAsync(billing, P0306Tests.Path(quote) + "/preview", command);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var response = await P0212Fixture.PostAsync(billing, P0306Tests.Path(quote), command);
        var body = await P0206Fixture.BodyAsync(response);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body.GetRawText());
        return body;
    }
}
