using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0611Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    internal const int MaxBytes = 10 * 1024 * 1024;
    internal const string Reference = "FIXTURE-INV-114";
    internal const string Issuer = "Fixture Accounting";
    internal static readonly DateTimeOffset Instant = new(2027, 3, 10, 18, 29, 59, TimeSpan.Zero);
    internal static DateOnly IssueDate => DateOnly.FromDateTime(Instant.UtcDateTime);

    [Fact]
    public async Task UploadAttachesImmutablePdfToItsAccountOrder()
    {
        await using var fixture = await StartAsync(database);
        var purchase = await P0311Tests.VerifiedAsync(fixture);
        using var billing = await SignInBillingAsync(fixture);
        var path = Path(purchase.Quote);
        var bytes = Pdf();
        var metadata = Metadata(bytes);
        var preview = await PreviewAsync(billing, path, metadata);
        Assert.Equal(1, preview.GetProperty("paymentConfirmations").GetInt32());
        using var empty = await billing.SendAsync(HttpMethod.Get, path);
        Assert.Empty((await ReadAsync(empty, HttpStatusCode.OK)).GetProperty("documents").EnumerateArray());

        using var uploaded = await UploadAsync(billing, path, Command(metadata, Digest(preview)));
        var receipt = await ReadAsync(uploaded, HttpStatusCode.Created);
        Assert.True(uploaded.Headers.CacheControl?.NoStore);
        Assert.Equal(1, receipt.GetProperty("version").GetInt32());
        Assert.Equal(Reference, receipt.GetProperty("issuerReference").GetString());
        Assert.Equal(purchase.Account, receipt.GetProperty("accountId").GetGuid());
        Assert.Equal(purchase.Quote.OrderId, receipt.GetProperty("orderId").GetGuid());
        Assert.False(receipt.TryGetProperty("content", out _));
        using var list = await billing.SendAsync(HttpMethod.Get, path);
        var listed = await ReadAsync(list, HttpStatusCode.OK);
        Assert.Equal(receipt.GetRawText(), Assert.Single(listed.GetProperty("documents").EnumerateArray()).GetRawText());
        Assert.Equal(bytes, await StoredBytesAsync(fixture));
        Assert.Equal(Hash(bytes), await ScalarAsync(fixture, "SELECT content_sha256 FROM billing_document_versions"));
        Assert.Equal(1L, await fixture.CountAsync("payment_confirmations"));
    }

    [Theory]
    [InlineData("html", 400, "BillingDocument.ContentInvalid")]
    [InlineData("markers-only", 400, "BillingDocument.ContentInvalid")]
    [InlineData("missing-eof", 400, "BillingDocument.ContentInvalid")]
    [InlineData("empty", 400, "BillingDocument.ContentInvalid")]
    [InlineData("oversized", 413, "BillingDocument.TooLarge")]
    [InlineData("media", 415, "BillingDocument.MediaTypeUnsupported")]
    [InlineData("hash", 400, "BillingDocument.ContentMismatch")]
    [InlineData("length", 400, "BillingDocument.ContentMismatch")]
    [InlineData("filename", 400, "BillingDocument.Invalid")]
    [InlineData("issuer", 400, "BillingDocument.Invalid")]
    [InlineData("unconfirmed", 400, "BillingDocument.Invalid")]
    public async Task InvalidOversizedAndNonPdfContentIsRejected(string invalid, int status, string code)
    {
        await using var fixture = await StartAsync(database);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        using var billing = await SignInBillingAsync(fixture);
        var path = Path(purchase.Quote);
        var valid = Pdf();
        var metadata = Metadata(valid);
        var bytes = invalid switch
        {
            "html" => "<html>Invoice</html>"u8.ToArray(),
            "markers-only" => "%PDF-1.7\nInvoice text\n%%EOF\n"u8.ToArray(),
            "missing-eof" => valid[..^6],
            "empty" => [],
            "oversized" => new byte[MaxBytes + 1],
            _ => valid,
        };
        var command = Command(metadata, new string('a', 64));
        if (invalid == "hash") command["sha256"] = new string('b', 64);
        if (invalid == "length") command["byteLength"] = valid.Length + 1;
        if (invalid == "filename") command["fileName"] = "../invoice.pdf";
        if (invalid == "issuer") command["issuerName"] = "Fixture\nIssuer";
        if (invalid == "unconfirmed") command["confirmed"] = false;
        using var response = await UploadAsync(billing, path, command, bytes, invalid == "media" ? "text/html" : "application/pdf");
        Assert.Equal(code, (await ReadAsync(response, (HttpStatusCode)status)).GetProperty("code").GetString());
        Assert.Equal(0L, await fixture.CountAsync("billing_document_versions"));
    }

    internal static Task<P0302Fixture> StartAsync(LicensingPostgreSqlFixture database, bool browser = false) =>
        P0302Fixture.StartAsync(database, new P0201Clock { Now = Instant }, P0306Tests.Settings, browser: browser);

    internal static async Task<P0110CookieBrowser> SignInBillingAsync(P0302Fixture fixture)
    {
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        var seed = await EnrollAsync(fixture, owner, P0306Tests.BillingEmail, "billing");
        return await fixture.Trial.Catalogue.SignInOperatorAsync(seed, P0306Tests.BillingEmail);
    }

    internal static async Task<string> EnrollAsync(P0302Fixture fixture, P0110CookieBrowser owner, string email, string capability)
    {
        var seed = await fixture.Trial.Catalogue.EnrollOperatorAsync(owner, email);
        await P0206Fixture.GrantAsync(owner, email, capability);
        return seed;
    }

    internal static string Path(CheckoutQuoteV1 quote) =>
        $"/api/admin/accounts/{quote.AccountId:D}/orders/{quote.OrderId:D}/billing-documents";

    internal static Dictionary<string, object> Metadata(byte[] bytes) => new()
    {
        ["kind"] = "AccountingInvoice", ["fileName"] = "fixture-invoice.pdf", ["mediaType"] = "application/pdf",
        ["byteLength"] = bytes.Length, ["sha256"] = Hash(bytes), ["issuerName"] = Issuer,
        ["issuerReference"] = Reference, ["issuedOn"] = IssueDate,
    };

    private static Dictionary<string, object> Command(Dictionary<string, object> metadata, string digest) =>
        new(metadata) { ["operationKey"] = Guid.NewGuid(), ["previewDigest"] = digest, ["confirmed"] = true };

    private static string Digest(JsonElement preview) => preview.GetProperty("previewDigest").GetString()!;

    private static async Task<JsonElement> PreviewAsync(P0110CookieBrowser billing, string path, object metadata)
    {
        using var response = await P0212Fixture.PostAsync(billing, path + "/preview", metadata);
        return await ReadAsync(response, HttpStatusCode.OK);
    }

    private static async Task<HttpResponseMessage> UploadAsync(P0110CookieBrowser billing, string path,
        object command, byte[]? bytes = null, string mediaType = "application/pdf", string? proof = null,
        string origin = P0110CookieBrowser.Origin)
    {
        using var form = new MultipartFormDataContent();
        form.Add(JsonContent.Create(command), "command");
        var content = new ByteArrayContent(bytes ?? Pdf());
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        form.Add(content, "content", "fixture-invoice.pdf");
        return await billing.SendAsync(HttpMethod.Post, path, form,
            proof is null ? await P0206Fixture.ProofAsync(billing) : proof.Length == 0 ? null : proof, origin);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected, string code) =>
        Assert.Equal(code, (await ReadAsync(response, expected)).GetProperty("code").GetString());

    internal static async Task<JsonElement> AttachAsync(P0110CookieBrowser billing, string path, byte[] bytes)
    {
        var metadata = Metadata(bytes);
        var preview = await PreviewAsync(billing, path, metadata);
        using var reply = await UploadAsync(billing, path, Command(metadata, Digest(preview)), bytes);
        return await ReadAsync(reply, HttpStatusCode.Created);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"Expected {(int)expected}, got {(int)response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.Clone();
    }

    internal static Task<object?> ScalarAsync(P0302Fixture fixture, string sql) =>
        LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner, sql);

    internal static async Task<byte[]> StoredBytesAsync(P0302Fixture fixture) =>
        (byte[])(await ScalarAsync(fixture, "SELECT content FROM billing_document_versions"))!;

    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static int PaddingForSize(int size)
    {
        // The large-file fixture adds one comment and grows the terminal xref offset from three to eight digits.
        return size - Pdf().Length - 2 - 5;
    }

    internal static byte[] Pdf(int? length = null)
    {
        const string drawing = "BT /F1 20 Tf 36 110 Td (Fixture invoice) Tj ET";
        string[] bodies = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 160] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", $"<< /Length {drawing.Length} >>\nstream\n{drawing}\nendstream"];
        var pdf = new StringBuilder("%PDF-1.7\n");
        if (length is { } size) pdf.Append('%').Append(' ', PaddingForSize(size)).Append('\n');
        List<int> offsets = [];
        foreach (var body in bodies)
        {
            offsets.Add(pdf.Length);
            pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }
        var start = pdf.Length;
        pdf.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        pdf.Append(System.Globalization.CultureInfo.InvariantCulture,
            $"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{start}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
