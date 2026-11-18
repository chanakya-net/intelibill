using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Intelibill.Infrastructure.Data;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0311Tests
{
    [Fact]
    public void DocumentMatchesPinnedContractAcrossCulturesAndNeverClaimsTaxIssuance()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectPaths.Root, "tests/contracts/licensing/payment-confirmation-v1.json")));
        var snapshot = contract.RootElement.GetProperty("snapshot").Deserialize<PaymentConfirmationSnapshotV1>(JsonSerializerOptions.Web)!;
        var expected = Encoding.UTF8.GetBytes(contract.RootElement.GetProperty("rendered").GetString()!);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "en-IN", "ar-SA", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Assert.Equal(expected, PaymentConfirmationBuilder.Render(snapshot));
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Assert.False(PaymentConfirmationsV1.IsValid(null, snapshot.Receipt.AccountId, snapshot.Receipt.OrderId));
        var document = Encoding.UTF8.GetString(expected);
        Assert.DoesNotContain("GSTIN", document, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoice No", document, StringComparison.Ordinal);
        Assert.DoesNotContain("termStartsAt", document, StringComparison.Ordinal);
        Assert.True(PaymentConfirmationsV1.IsValid(snapshot.Receipt, snapshot.Receipt.AccountId, snapshot.Receipt.OrderId));
        var licensing = Path.Combine(ProjectPaths.Root, "src/licensing");
        foreach (var path in Directory.EnumerateFiles(licensing, "*.cs", SearchOption.AllDirectories).Where(path => !path.Contains("/obj/", StringComparison.Ordinal)))
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain("invoice_sequences", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Intelibill.Domain.Entities", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FullConfirmationPageFitsRelayBoundAtMaximumContractValues()
    {
        var account = Guid.NewGuid();
        var order = Guid.NewGuid();
        var rows = Enumerable.Range(0, PaymentConfirmationsV1.PageSize).Select(_ => new PaymentConfirmationV1(
            Guid.NewGuid(), account, order, "Manual", CheckoutOrdersV1.MaxPaise, "INR",
            P0209Fixture.Utc("9998-12-31T23:59:59.999999Z"), P0209Fixture.Utc("9998-12-31T23:59:59.999999Z"), "Verification")).ToArray();
        Assert.All(rows, row => Assert.True(PaymentConfirmationsV1.IsValid(row, account, order)));
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(rows, JsonSerializerOptions.Web).Length, 1, PaymentConfirmationsV1.MaxListBytes);
    }

    private static bool ValidDocument(HttpResponseMessage response, Guid id) =>
        (bool)typeof(ApplicationDbContext).Assembly.GetType("Intelibill.Infrastructure.Billing.MerchantBillingClient")!
            .GetMethod("IsValidDocument", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [response, id])!;

    [Fact]
    public void MerchantRelayRejectsWrongContentTypeLengthAndAttachmentIdentity()
    {
        var id = Guid.NewGuid();
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("PAYMENT CONFIRMATION", Encoding.UTF8, "text/plain") };
        response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
        response.Headers.Add("X-Content-Type-Options", "nosniff");
        response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = PaymentConfirmationsV1.FileName(id) };
        Assert.True(ValidDocument(response, id));
        response.Content.Headers.ContentType!.MediaType = "text/html";
        Assert.False(ValidDocument(response, id));
        response.Content.Headers.ContentType.MediaType = "text/plain";
        response.Content.Headers.ContentLength = PaymentConfirmationsV1.MaxDocumentBytes + 1;
        Assert.False(ValidDocument(response, id));
        response.Content.Headers.ContentLength = 20;
        response.Content.Headers.ContentDisposition.FileName = "../foreign.txt";
        Assert.False(ValidDocument(response, id));
        response.Content.Headers.ContentDisposition.FileName = PaymentConfirmationsV1.FileName(id);
        response.Content.Headers.ContentDisposition.DispositionType = "inline";
        Assert.False(ValidDocument(response, id));
    }
    [Fact]
    public void MerchantRelayAcceptsCompleteNamedCoverageLargerThan64KiB()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectPaths.Root, "tests/contracts/licensing/payment-confirmation-v1.json")));
        var original = contract.RootElement.GetProperty("snapshot").Deserialize<PaymentConfirmationSnapshotV1>(JsonSerializerOptions.Web)!;
        var shops = Enumerable.Range(0, 300).Select(_ => new CheckoutShopV1(Guid.NewGuid(), new string('店', 250), null)).ToArray();
        var snapshot = original with { Terms = original.Terms with { ShopCount = shops.Length, Shops = shops } };
        var bytes = PaymentConfirmationBuilder.Render(snapshot);
        Assert.True(bytes.Length > 65536);
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" };
        response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
        response.Headers.Add("X-Content-Type-Options", "nosniff");
        response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = PaymentConfirmationsV1.FileName(snapshot.Receipt.ConfirmationId) };
        Assert.True(ValidDocument(response, snapshot.Receipt.ConfirmationId));
        Assert.Equal(shops.Length, Encoding.UTF8.GetString(bytes).Split("Covered shop:").Length - 1);
    }

}
