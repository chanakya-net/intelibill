using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0307Tests
{
    [Fact]
    public async Task CaptureOnWrongReceivingAccountIsDiagnosticOnly()
    {
        await using var fixture = await StartAsync(database, new P0201Clock { Now = P0210Fixture.Start });
        var (payer, account, _, quote) = await fixture.AcceptedQuoteAsync(1);
        var started = await StartAsync(payer.Client, quote, HttpStatusCode.Created);
        var id = started.GetProperty("attemptId").GetGuid();
        await CaptureAsync(payer.Client, quote, id);
        await using var scope = fixture.Hosts.Licensing.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<IPaymentStore>();
        var loaded = await store.InAttemptScopeAsync<StoredPaymentAttempt>(account, id, false, async ct =>
            await store.FindAttemptAsync(account, id, false, ct) is { } attempt ? attempt : Error.NotFound(), CancellationToken.None);
        var original = loaded.Value;
        var gateway = services.GetRequiredService<IPaymentGatewayResolver>().ForPin(original.Pin)!;
        var result = await gateway.VerifyCheckoutAsync(
            new CheckoutVerification(original.ProviderOrderReference!, original.AmountPaise, original.Currency), CancellationToken.None);
        var observation = Assert.IsType<PaymentObservation>(result.Value);
        var foreign = observation with { Pin = observation.Pin with { ProviderAccountId = "different-fixture-account" } };
        var refused = await services.GetRequiredService<FulfilPaidOrderHandler>().HandleAsync(original, foreign, CancellationToken.None);
        Assert.Equal(FulfilmentOutcome.Rejected, refused.Value);
        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilments"));
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        using var finance = await P0306Tests.SignInBillingAsync(fixture);
        var diagnostic = await SingleCaseAsync(finance, account);
        Assert.Equal("ReceivingPinMismatch", diagnostic.GetProperty("classification").GetString());
    }

    [Fact]
    public async Task LaterExactReadOfSamePaymentFulfilsAndRetainsOriginalDiscrepancy()
    {
        var probe = new P0302Tests.GatewayProbe();
        await using var fixture = await StartAsync(database, new P0201Clock { Now = P0210Fixture.Start },
            services: probe.Install);
        probe.Database = fixture.Hosts.LicensingOwner;
        var (payer, account, _, quote) = await fixture.AcceptedQuoteAsync(1);
        probe.Account = account;
        var attempt = (await StartAsync(payer.Client, quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        await CaptureAsync(payer.Client, quote, attempt);
        probe.Tamper = payment => payment with { AmountPaise = payment.AmountPaise - 1 };
        AssertStatus(await VerifyAsync(payer.Client, quote, attempt), "PendingVerification", "NotFulfilled", "NotApplicable");
        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilments"));

        probe.Tamper = null;
        AssertStatus(await VerifyAsync(payer.Client, quote, attempt), "Verified", "Fulfilled", "Applying");
        Assert.Equal(1L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments"));
        using var finance = await P0306Tests.SignInBillingAsync(fixture);
        var diagnostic = await SingleCaseAsync(finance, account);
        Assert.Equal("Underpayment", diagnostic.GetProperty("classification").GetString());
        Assert.Equal(quote.AmountPaise - 1, diagnostic.GetProperty("observedAmountPaise").GetInt64());
        using var response = await finance.SendAsync(HttpMethod.Get,
            $"{ReviewPath(account)}/{diagnostic.GetProperty("caseId").GetGuid():D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<JsonElement>();
        var references = detail.GetProperty("references");
        Assert.Equal(quote.AmountPaise, references.GetProperty("verifiedAmountPaise").GetInt64());
        Assert.Equal(quote.Currency, references.GetProperty("verifiedCurrency").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoCapturesFirstSeenTogetherCannotChooseOneForAccess(bool split)
    {
        await using var rig = await P0304Tests.Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        var order = rig.Fake.OrderId();
        var gross = rig.Quote.AmountPaise;
        rig.Fake.AddPayment(order, "captured", amount: split ? gross / 2 : gross);
        rig.Fake.AddPayment(order, "captured", amount: split ? gross - gross / 2 : gross);
        for (var count = 0; count < 2; count++)
            AssertStatus(await rig.VerifyAsync(attempt, null, null), "PendingVerification", "NotFulfilled", "NotApplicable");
        var classification = split ? "Underpayment" : "DuplicateFunds";
        Assert.Equal(2L, await rig.Fixture.CountAsync("unmatched_payments", $"classification = '{classification}'"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("payment_fulfilments"));
    }
}
