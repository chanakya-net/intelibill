using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0609Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0610Tests
{
    [Fact]
    public async Task PreflightFailuresKeepSendableUntilRecoveredAndNeverPostTwice()
    {
        var (fixture, fake, package, request, _, _, _) = await StartRazorpayRefundAsync();
        await using var _ = fixture;
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await ApproveEndAsync(finance, package.Account, request, Instant.AddDays(3));

        fake.FailOrderReads = 1;
        fake.FailPaymentReads = 1;
        await PumpRefundsAsync(fixture);
        Assert.Equal(0L, await fixture.CountAsync("refund_sends"));
        Assert.Equal(0, RefundPosts(fake));
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Reserved'"));

        fixture.At(Instant.AddMinutes(5));
        await PumpRefundsAsync(fixture);
        Assert.Equal(0L, await fixture.CountAsync("refund_sends"));
        Assert.Equal(0, RefundPosts(fake));
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Reserved'"));

        // Recovery performs preflight, commits one send right, and issues one POST whose reply is lost.
        fake.TruncateWhen = call => call.EndsWith("/refund", StringComparison.Ordinal);
        fixture.At(Instant.AddMinutes(20));
        await PumpRefundsAsync(fixture);
        Assert.Equal(1L, await fixture.CountAsync("refund_sends"));
        Assert.Equal(1, RefundPosts(fake));
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Unknown'"));

        // Lease takeover after send never permits another POST; the next pump confirms by GET only.
        var runtime = fixture.Hosts.Database.Login(fixture.Hosts.Fixture.Owner.Database!, "licensing_runtime");
        var expiredToken = Guid.NewGuid();
        var takeoverToken = Guid.NewGuid();
        Assert.Equal(true, await ScalarAsync(runtime, "SELECT sent FROM public.claim_refund($1,$2)", Instant.AddHours(1), expiredToken));
        Assert.Equal(true, await ScalarAsync(runtime, "SELECT sent FROM public.claim_refund($1,$2)", Instant.AddHours(1).AddMinutes(2), takeoverToken));
        Assert.Equal(false, await ScalarAsync(runtime, "SELECT public.start_refund_send($1,$2)", takeoverToken, Instant.AddHours(1).AddMinutes(2)));

        fixture.At(Instant.AddHours(2));
        await PumpRefundsAsync(fixture);
        Assert.Equal(1, RefundPosts(fake));
        Assert.Equal(1L, await fixture.CountAsync("refund_sends"));
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Confirmed'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_applications", "disposition='Applied'"));
    }

    [Fact]
    public async Task SignedRefundHintRecoversCandidateBeyondBoundedList()
    {
        var (fixture, fake, package, request, _, _, payment) = await StartRazorpayRefundAsync();
        await using var _ = fixture;
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var receipt = await ApproveAndReadReceiptAsync(finance, package.Account, request, 4500);
        for (var i = 0; i < 100; i++) fake.Refunds.Add(fake.RefundRecord(payment, Guid.NewGuid().ToString("N"), 100));

        fake.TruncateWhen = call => call.EndsWith("/refund", StringComparison.Ordinal);
        await PumpRefundsAsync(fixture);
        Assert.Equal(1, RefundPosts(fake));
        var remoteId = fake.Refunds.Single(r => r["receipt"]!.GetValue<string>() == receipt)["id"]!.GetValue<string>();

        fixture.At(Instant.AddMinutes(5));
        await PumpRefundsAsync(fixture);
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Unknown'"));
        Assert.Equal(0, DirectRefundGets(fake, payment, remoteId));

        using var ingress = fixture.Hosts.Licensing.CreateClient();
        using var hint = await P0305Tests.SendAsync(ingress, RefundEventBody(payment, remoteId, receipt, 4500), "evt_candidate_hint");
        Assert.Equal(HttpStatusCode.NoContent, hint.StatusCode);
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Unknown'"));
        Assert.Equal(0L, await fixture.CountAsync("refund_bindings"));

        await PumpRefundsAsync(fixture);
        Assert.Equal(1, RefundPosts(fake));
        Assert.Equal(1, DirectRefundGets(fake, payment, remoteId));
        Assert.Equal(1L, await fixture.CountAsync("refund_work", $"state='Confirmed' AND refund_reference='{remoteId}'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_bindings", $"reference='{remoteId}'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_applications", "disposition='Applied'"));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task LostFirstGetAfterSuccessfulPostRetainsCandidateForDirectRecovery(HttpStatusCode failure)
    {
        var (fixture, fake, package, request, _, _, payment) = await StartRazorpayRefundAsync();
        await using var _ = fixture;
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var receipt = await ApproveAndReadReceiptAsync(finance, package.Account, request, 4500);
        for (var i = 0; i < 100; i++) fake.Refunds.Add(fake.RefundRecord(payment, Guid.NewGuid().ToString("N"), 100));

        fake.RefundGetFailure = failure;
        fake.FailRefundGets = 1;
        await PumpRefundsAsync(fixture);
        var remoteId = fake.Refunds.Single(r => r["receipt"]!.GetValue<string>() == receipt)["id"]!.GetValue<string>();
        Assert.Equal(1, RefundPosts(fake));
        Assert.Equal(1, DirectRefundGets(fake, payment, remoteId));
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Unknown' AND refund_reference IS NULL"));
        Assert.Equal(0L, await fixture.CountAsync("refund_bindings"));

        fixture.At(Instant.AddMinutes(5));
        await PumpRefundsAsync(fixture);
        Assert.Equal(1, RefundPosts(fake));
        Assert.Equal(2, DirectRefundGets(fake, payment, remoteId));
        Assert.Equal(1L, await fixture.CountAsync("refund_work", $"state='Confirmed' AND refund_reference='{remoteId}'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_bindings", $"reference='{remoteId}'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_applications", "disposition='Applied'"));
    }

    [Fact]
    public async Task MismatchedCandidatePreservesHoldAndExistingBinding()
    {
        var (fixture, fake, package, first, confirmation, order, payment) = await StartRazorpayRefundAsync();
        await using var _ = fixture;
        var second = await RequestRefundAsync(package, order, confirmation, 4500);
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);

        var firstReceipt = await ApproveAndReadReceiptAsync(finance, package.Account, first, 4500);
        await PumpRefundsAsync(fixture);
        var boundId = fake.Refunds.Single(r => r["receipt"]!.GetValue<string>() == firstReceipt)["id"]!.GetValue<string>();
        Assert.Equal(1L, await fixture.CountAsync("refund_bindings", $"reference='{boundId}'"));

        for (var i = 0; i < 100; i++) fake.Refunds.Add(fake.RefundRecord(payment, Guid.NewGuid().ToString("N"), 100));
        var secondReceipt = await ApproveAndReadReceiptAsync(finance, package.Account, second, 4500);
        fake.TruncateWhen = call => call.EndsWith("/refund", StringComparison.Ordinal);
        await PumpRefundsAsync(fixture);
        var secondRemoteId = fake.Refunds.Single(r => r["receipt"]!.GetValue<string>() == secondReceipt)["id"]!.GetValue<string>();

        using var ingress = fixture.Hosts.Licensing.CreateClient();
        // Mismatched hint targeting unbound second with first's already-bound id, and targeting bound first with second's id.
        using var mismatchUnbound = await P0305Tests.SendAsync(ingress, RefundEventBody(payment, boundId, secondReceipt, 4500), "evt_mismatch_unbound");
        using var mismatchBound = await P0305Tests.SendAsync(ingress, RefundEventBody(payment, secondRemoteId, firstReceipt, 4500), "evt_mismatch_bound");
        Assert.Equal(HttpStatusCode.NoContent, mismatchUnbound.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, mismatchBound.StatusCode);

        fixture.At(Instant.AddMinutes(5));
        await PumpRefundsAsync(fixture);
        Assert.Equal(1L, await fixture.CountAsync("refund_bindings", $"reference='{boundId}'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_bindings"));
        Assert.Equal(1L, await fixture.CountAsync("refund_work", $"state='Confirmed' AND refund_reference='{boundId}'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Unknown' AND refund_reference IS NULL"));

        Assert.Equal(0L, await fixture.CountAsync("refund_work", "candidate_reference IS NOT NULL"));

        // Delivering the authentic id afterwards still recovers and confirms the held second reservation without a new POST.
        using var validHint = await P0305Tests.SendAsync(ingress, RefundEventBody(payment, secondRemoteId, secondReceipt, 4500), "evt_valid_second");
        Assert.Equal(HttpStatusCode.NoContent, validHint.StatusCode);
        await PumpRefundsAsync(fixture);
        Assert.Equal(2, RefundPosts(fake));
        Assert.Equal(2L, await fixture.CountAsync("refund_bindings"));
        Assert.Equal(2L, await fixture.CountAsync("refund_work", "state='Confirmed'"));
    }

    private async Task<(P0308Fixture Fixture, FakeRazorpay Fake, Package Package, Guid Request, Guid Confirmation, Guid Order, string Payment)>
        StartRazorpayRefundAsync(long requestPaise = 6000, Dictionary<string, string?>? extraSettings = null,
            string? keyId = null, string? keySecret = null)
    {
        var fake = new FakeRazorpay(keyId ?? FakeRazorpay.KeyId, keySecret ?? FakeRazorpay.KeySecret);
        var settings = FakeRazorpay.Settings();
        settings["Payments:Razorpay:WebhookSecret"] = P0305Tests.Secret;
        settings["Payments:Razorpay:WebhookAccountId"] = P0305Tests.Account;
        if (extraSettings is not null)
            foreach (var (k, v) in extraSettings) settings[k] = v;
        var fixture = await P0609Tests.StartAsync(database, Instant, settings, razorpayHandler: fake);
        fake.Clock = () => fixture.Checkout.CheckoutClock.Now;
        var package = await fixture.PackageAsync(1, ["Razorpay Refund Shop"]);
        var quote = await AcceptAsync(package, await FirstQuoteAsync(package, "Month"));
        var attempt = await StartPaymentAsync(package, quote);
        var payment = fake.AddPayment(fake.OrderId(), "captured");
        using var verify = await package.Payer.Client.PostAsJsonAsync(P0302Fixture.VerifyPath(quote, attempt),
            new { launcherOutcome = (string?)null, launcherProof = (Dictionary<string, string>?)null });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        await P0608Tests.DrainAsync(fixture);
        var confirmation = await P0608Tests.ConfirmationIdAsync(package.Payer.Client, package.Account, quote.OrderId);
        var request = await RequestRefundAsync(package, quote.OrderId, confirmation, requestPaise);
        return (fixture, fake, package, request, confirmation, quote.OrderId, payment);
    }

    private static async Task<string> ApproveAndReadReceiptAsync(P0110CookieBrowser finance, Guid account, Guid request, long paise)
    {
        var preview = await PreviewAsync(finance, account, request, paise, "Preserve");
        var approved = await ReadAsync(await ApproveAsync(finance, account, request, Command(preview, paise, "Preserve")),
            HttpStatusCode.Created);
        return approved.GetProperty("reservationId").GetGuid().ToString("N");
    }

    private static int RefundPosts(FakeRazorpay fake) =>
        fake.Calls.Count(c => c.StartsWith("POST /v1/payments/", StringComparison.Ordinal) && c.EndsWith("/refund", StringComparison.Ordinal));

    private static int DirectRefundGets(FakeRazorpay fake, string payment, string refundId) =>
        fake.Calls.Count(c => c == $"GET /v1/payments/{payment}/refunds/{refundId}");

    private static string RefundEventBody(string payment, string refundId, string receipt, long amount) => JsonSerializer.Serialize(new
    {
        entity = "event",
        account_id = P0305Tests.Account,
        @event = "refund.processed",
        payload = new
        {
            refund = new
            {
                entity = new
                {
                    entity = "refund",
                    id = refundId,
                    payment_id = payment,
                    receipt,
                    amount,
                    currency = "INR",
                    status = "processed",
                },
            },
        },
    });
}
