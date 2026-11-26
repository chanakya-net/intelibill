using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0612Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task MemberDownloadsAttachedInvoiceWithRecordedHash()
    {
        await using var fixture = await P0611Tests.StartAsync(database);
        var purchase = await P0311Tests.VerifiedAsync(fixture);
        using var billing = await P0611Tests.SignInBillingAsync(fixture);
        var bytes = P0611Tests.Pdf();
        var uploaded = await P0611Tests.AttachAsync(billing, P0611Tests.Path(purchase.Quote), bytes);
        var versionId = uploaded.GetProperty("versionId").GetGuid();
        var expectedHash = P0611Tests.Hash(bytes);

        var listPath = DocumentsPath(purchase.Quote);
        using var listResponse = await purchase.Payer.Client.GetAsync(listPath);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.True(listResponse.Headers.CacheControl?.NoStore);
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var listed = Assert.Single(listBody.GetProperty("documents").EnumerateArray());
        Assert.Equal(versionId, listed.GetProperty("versionId").GetGuid());
        Assert.Equal(expectedHash, listed.GetProperty("sha256").GetString());

        using var download = await purchase.Payer.Client.GetAsync($"{listPath}/{versionId:D}/content");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.True(download.Headers.CacheControl?.NoStore);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal($"accounting-invoice-{versionId:D}.pdf",
            download.Content.Headers.ContentDisposition?.FileNameStar ?? download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Contains("nosniff", download.Headers.GetValues("X-Content-Type-Options"));
        Assert.Contains("default-src 'none'; sandbox", download.Headers.GetValues("Content-Security-Policy"));
        var downloaded = await download.Content.ReadAsByteArrayAsync();
        Assert.Equal(bytes, downloaded);
        Assert.Equal(expectedHash, P0611Tests.Hash(downloaded));
        Assert.Equal(expectedHash, await P0611Tests.ScalarAsync(fixture, "SELECT content_sha256 FROM billing_document_versions"));
    }

    [Fact]
    public async Task MemberListsOnlyTruthfulInvoiceMetadataForTheirOrder()
    {
        await using var fixture = await P0611Tests.StartAsync(database);
        var purchase = await P0311Tests.VerifiedAsync(fixture);
        var withoutInvoice = await P0311Tests.VerifiedAsync(fixture);
        using var billing = await P0611Tests.SignInBillingAsync(fixture);
        var bytes = P0611Tests.Pdf();
        await P0611Tests.AttachAsync(billing, P0611Tests.Path(purchase.Quote), bytes);

        using var listResponse = await purchase.Payer.Client.GetAsync(DocumentsPath(purchase.Quote));
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.True(listResponse.Headers.CacheControl?.NoStore);
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, list.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(purchase.Account, list.GetProperty("accountId").GetGuid());
        Assert.Equal(purchase.Quote.OrderId, list.GetProperty("orderId").GetGuid());
        var row = Assert.Single(list.GetProperty("documents").EnumerateArray());
        Assert.Equal(BillingDocumentsV1.AccountingInvoice, row.GetProperty("kind").GetString());
        Assert.Equal(BillingDocumentsV1.MediaType, row.GetProperty("mediaType").GetString());
        Assert.Equal(P0611Tests.Issuer, row.GetProperty("issuerName").GetString());
        Assert.Equal(P0611Tests.Reference, row.GetProperty("issuerReference").GetString());
        Assert.Equal("2027-03-10", row.GetProperty("issuedOn").GetString());
        Assert.Equal(bytes.Length, row.GetProperty("byteLength").GetInt32());
        Assert.Equal(P0611Tests.Hash(bytes), row.GetProperty("sha256").GetString());
        Assert.Equal(1, row.GetProperty("version").GetInt32());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("supersedesVersionId").ValueKind);
        foreach (var secret in new[] { "uploadedBy", "mfaVerifiedAt", "operationKey", "fileName", "content", "payloadHash", "adminId", "sessionId" })
            Assert.False(row.TryGetProperty(secret, out _), $"Unexpected internal field {secret} exposed to customer.");

        using var emptyResponse = await withoutInvoice.Payer.Client.GetAsync(DocumentsPath(withoutInvoice.Quote));
        Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        Assert.True(emptyResponse.Headers.CacheControl?.NoStore);
        Assert.Empty((await emptyResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documents").EnumerateArray());

        var confirmations = await purchase.Payer.Client.GetFromJsonAsync<JsonElement>(
            P0302Fixture.PaymentPath(purchase.Quote) + "/confirmations");
        var confirmation = Assert.Single(confirmations.EnumerateArray());
        Assert.False(confirmation.TryGetProperty("issuerReference", out _));
        Assert.DoesNotContain(BillingDocumentsV1.AccountingInvoice, confirmations.GetRawText(), StringComparison.Ordinal);
    }

    internal static string DocumentsPath(CheckoutQuoteV1 quote) =>
        $"/api/billing/accounts/{quote.AccountId:D}/orders/{quote.OrderId:D}/billing-documents";
}
