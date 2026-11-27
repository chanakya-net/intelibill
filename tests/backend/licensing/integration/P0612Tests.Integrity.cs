using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0612Tests
{
    [Fact]
    public async Task BytesAndHashStayIdenticalAcrossRepeatedReadsAndLaterEdits()
    {
        await using var fixture = await P0611Tests.StartAsync(database);
        var purchase = await P0311Tests.VerifiedAsync(fixture);
        using var billing = await P0611Tests.SignInBillingAsync(fixture);
        var pdf = P0611Tests.Pdf();
        var uploaded = await P0611Tests.AttachAsync(billing, P0611Tests.Path(purchase.Quote), pdf);
        var versionId = uploaded.GetProperty("versionId").GetGuid();

        var listPath = DocumentsPath(purchase.Quote);
        var contentPath = $"{listPath}/{versionId:D}/content";
        var originalList = (await purchase.Payer.Client.GetFromJsonAsync<JsonElement>(listPath)).GetRawText();
        var firstBytes = await purchase.Payer.Client.GetByteArrayAsync(contentPath);
        Assert.Equal(pdf, firstBytes);

        await P0311Tests.MutateAsync(fixture, purchase);
        fixture.CheckoutClock.Now += TimeSpan.FromDays(15);

        var secondList = (await purchase.Payer.Client.GetFromJsonAsync<JsonElement>(listPath)).GetRawText();
        var secondBytes = await purchase.Payer.Client.GetByteArrayAsync(contentPath);
        Assert.Equal(originalList, secondList);
        Assert.Equal(firstBytes, secondBytes);
        Assert.Equal(P0611Tests.Hash(pdf), P0611Tests.Hash(secondBytes));
    }

    [Fact]
    public async Task MaximumSizeDocumentPassesTheRelayBound()
    {
        await using var fixture = await P0611Tests.StartAsync(database);
        var purchase = await P0311Tests.VerifiedAsync(fixture);
        using var billing = await P0611Tests.SignInBillingAsync(fixture);
        var maxPdf = P0611Tests.Pdf(P0611Tests.MaxBytes);
        Assert.Equal(P0611Tests.MaxBytes, maxPdf.Length);
        var uploaded = await P0611Tests.AttachAsync(billing, P0611Tests.Path(purchase.Quote), maxPdf);
        var versionId = uploaded.GetProperty("versionId").GetGuid();

        using var response = await purchase.Payer.Client.GetAsync($"{DocumentsPath(purchase.Quote)}/{versionId:D}/content");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var downloaded = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(P0611Tests.MaxBytes, downloaded.Length);
        Assert.Equal(maxPdf, downloaded);
        Assert.Equal(P0611Tests.Hash(maxPdf), P0611Tests.Hash(downloaded));
    }
}
