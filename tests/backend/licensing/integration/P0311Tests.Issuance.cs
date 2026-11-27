using System.Net;
using System.Net.Http.Json;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0311Tests
{
    [Fact]
    public async Task ManualOnlineRaceKeepsBothReceiptConfirmationsWithOneTerm()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart }, P0306Tests.Settings);
        var (payer, _, _, quote) = await fixture.AcceptedQuoteAsync(1);
        var attempt = (await StartAsync(payer.Client, quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        await CaptureAsync(payer.Client, quote, attempt);
        using var billing = await P0306Tests.SignInBillingAsync(fixture);
        var command = P0306Tests.Receipt(quote, WholeSecondStart);
        var online = VerifyAsync(payer.Client, quote, attempt);
        var manual = P0212Fixture.PostAsync(billing, P0306Tests.Path(quote), command);
        await Task.WhenAll(online, manual);
        using var receipt = await manual;
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
        var rows = (await payer.Client.GetFromJsonAsync<PaymentConfirmationV1[]>(PaymentPath(quote) + "/confirmations"))!;
        Assert.Equal(2, rows.Length);
        Assert.Contains(rows, r => r.Source == "Online");
        Assert.Contains(rows, r => r.Source == "Manual");
        Assert.All(rows, r => { Assert.Equal(WholeSecondStart, r.VerifiedAt); Assert.Equal(r.VerifiedAt, r.IssuedAt); Assert.Equal("Verification", r.IssuanceBasis); });
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments"));
        Assert.Equal(1L, await fixture.CountAsync("payment_confirmations", "facts->>'fulfilmentAtIssuance' = 'NeedsReview'"));
        using var replay = await P0212Fixture.PostAsync(billing, P0306Tests.Path(quote), command);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => VerifyAsync(payer.Client, quote, attempt)));
        Assert.Equal(2L, await fixture.CountAsync("payment_confirmations"));
        foreach (var row in rows)
        {
            using var document = await payer.Client.GetAsync(PaymentPath(quote) + $"/confirmations/{row.ConfirmationId}/document");
            var text = await document.Content.ReadAsStringAsync();
            Assert.DoesNotContain(command.Reference, text, StringComparison.Ordinal);
            Assert.DoesNotContain(command.Evidence, text, StringComparison.Ordinal);
            Assert.Contains(quote.PlanName, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RejectedAmountNeverIssuesAConfirmation()
    {
        var probe = new P0302Tests.GatewayProbe();
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart }, services: probe.Install);
        var (payer, account, _, quote) = await fixture.AcceptedQuoteAsync(1);
        probe.Database = fixture.Hosts.LicensingOwner;
        probe.Account = account;
        var attempt = (await StartAsync(payer.Client, quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        await CaptureAsync(payer.Client, quote, attempt);
        probe.Tamper = p => p with { AmountPaise = p.AmountPaise - 1 };
        await VerifyAsync(payer.Client, quote, attempt);
        Assert.Equal(0L, await fixture.CountAsync("payment_confirmations"));
        probe.Tamper = null;
        await VerifyAsync(payer.Client, quote, attempt);
        Assert.Equal(1L, await fixture.CountAsync("payment_confirmations"));
    }

    [Fact]
    public async Task BillingOperatorReadsIdenticalBytesAndOtherCapabilitiesCannotRead()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart });
        var purchase = await VerifiedAsync(fixture);
        var rows = (await purchase.Payer.Client.GetFromJsonAsync<PaymentConfirmationV1[]>(PaymentPath(purchase.Quote) + "/confirmations"))!;
        var id = Assert.Single(rows).ConfirmationId;
        var expected = await purchase.Payer.Client.GetByteArrayAsync(PaymentPath(purchase.Quote) + $"/confirmations/{id}/document");
        var path = $"/api/admin/accounts/{purchase.Account}/orders/{purchase.Quote.OrderId}/payment-confirmations/{id}/document";
        using var billing = await P0306Tests.SignInBillingAsync(fixture);
        using var document = await billing.SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.Equal(expected, await document.Content.ReadAsByteArrayAsync());
        Assert.True(document.Headers.CacheControl?.NoStore);
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        using var denied = await owner.SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("ManualPayment.AccessDenied", (await denied.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("code").GetString());
        using var mismatchedClient = fixture.Hosts.Licensing.CreateClient();
        mismatchedClient.DefaultRequestHeaders.Add("X-Admin-Session", Guid.NewGuid().ToString("D"));
        using var wrongSession = billing.Fork(mismatchedClient);
        using var mismatched = await wrongSession.SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.Unauthorized, mismatched.StatusCode);
    }

    [Fact]
    public async Task SupportOnlyCannotReadBillingConfirmations()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart });
        var purchase = await VerifiedAsync(fixture);
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        var seed = await fixture.Trial.Catalogue.EnrollOperatorAsync(owner, "support.p0311@example.test");
        await fixture.Trial.Catalogue.GrantAsync("support.p0311@example.test", "support");
        using var support = await fixture.Trial.Catalogue.SignInOperatorAsync(seed, "support.p0311@example.test");
        using var refused = await support.SendAsync(HttpMethod.Get, $"/api/admin/accounts/{purchase.Account}/orders/{purchase.Quote.OrderId}/payment-confirmations");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }
}
