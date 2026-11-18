using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

[Collection(MerchantBillingHosts.Name)]
public sealed partial class P0311Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task VerifiedPaymentIssuesImmutableConfirmationAttachment()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new P0201Clock { Now = WholeSecondStart });
        var purchase = await fixture.AcceptedQuoteAsync(1);
        var (payer, _, _, quote) = purchase;
        var started = await P0302Fixture.StartAsync(payer.Client, quote, HttpStatusCode.Created);
        var attempt = started.GetProperty("attemptId").GetGuid();
        await CaptureAsync(payer.Client, quote, attempt);
        await VerifyAsync(payer.Client, quote, attempt);
        using var list = await payer.Client.GetAsync(PaymentPath(quote) + "/confirmations");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var confirmations = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(confirmations.EnumerateArray());
        var id = confirmations[0].GetProperty("confirmationId").GetGuid();
        using var document = await payer.Client.GetAsync(PaymentPath(quote) + $"/confirmations/{id}/document");
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.True(document.Headers.CacheControl?.NoStore);
        Assert.Equal("attachment", document.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("text/plain", document.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", document.Headers.GetValues("X-Content-Type-Options").Single());
        var text = await document.Content.ReadAsStringAsync();
        Assert.StartsWith("PAYMENT CONFIRMATION\n", text, StringComparison.Ordinal);
        Assert.Contains("not a tax invoice", text, StringComparison.Ordinal);
        Assert.Contains("not a merchant sale receipt", text, StringComparison.Ordinal);
        Assert.Contains(quote.AmountPaise.ToString(System.Globalization.CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        await VerifyAsync(payer.Client, quote, attempt);
        Assert.Equal(1L, await fixture.CountAsync("payment_confirmations"));
    }
}
