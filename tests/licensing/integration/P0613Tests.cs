using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0613Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    internal const string SecondReference = "FIXTURE-INV-114-CORR-1";
    internal const string ThirdReference = "FIXTURE-INV-114-CORR-2";
    internal const string SecondIssuer = "Fixture Accounting LLP";
    internal const string ThirdIssuer = "Fixture Accounting Revised";
    internal const string SecondReason = "Correct GSTIN and legal name on customer accounting invoice.";
    internal const string ThirdReason = "Reissue with corrected credit memo reference for finance ledger.";

    [Fact]
    public async Task CorrectionPreservesEveryIssuedVersionAndPayment()
    {
        var clock = new P0201Clock { Now = P0611Tests.Instant };
        await using var fixture = await P0302Fixture.StartAsync(database, clock, P0306Tests.Settings);
        var purchase = await P0311Tests.VerifiedAsync(fixture);
        using var billing = await P0611Tests.SignInBillingAsync(fixture);
        var adminPath = P0611Tests.Path(purchase.Quote);
        var customerPath = P0612Tests.DocumentsPath(purchase.Quote);

        var pdf1 = DistinctPdf("Original invoice v1");
        var pdf2 = DistinctPdf("Corrected invoice v2 with revised customer details");
        var pdf3 = DistinctPdf("Reissued successor invoice v3 with updated reference");
        Assert.Equal(3, new[] { P0611Tests.Hash(pdf1), P0611Tests.Hash(pdf2), P0611Tests.Hash(pdf3) }.Distinct().Count());

        var confirmationBefore = await ReadConfirmationEvidenceAsync(purchase);
        var fundingBefore = await FundingFactsAsync(fixture, purchase.Account, purchase.Quote.OrderId);

        var v1 = await P0611Tests.AttachAsync(billing, adminPath, pdf1);
        var documentId = v1.GetProperty("documentId").GetGuid();
        var v1Id = v1.GetProperty("versionId").GetGuid();

        clock.Now = P0611Tests.Instant.AddMinutes(2);
        var v2Meta = SupersedeMetadata(pdf2, "fixture-invoice-v2.pdf", SecondIssuer, SecondReference,
            P0611Tests.IssueDate, SecondReason);
        var v2Preview = await PreviewSupersedeAsync(billing, adminPath, v1Id, v2Meta);
        AssertSupersedePreview(v2Preview, purchase, documentId, v1Id, 1, 2, SecondReason);
        var v2 = await SupersedeAsync(billing, adminPath, v1Id, v2Meta, Digest(v2Preview), pdf2);
        var v2Id = v2.GetProperty("versionId").GetGuid();

        clock.Now = P0611Tests.Instant.AddMinutes(4);
        var v3Meta = SupersedeMetadata(pdf3, "fixture-invoice-v3.pdf", ThirdIssuer, ThirdReference,
            P0611Tests.IssueDate, ThirdReason);
        var v3Preview = await PreviewSupersedeAsync(billing, adminPath, v2Id, v3Meta);
        AssertSupersedePreview(v3Preview, purchase, documentId, v2Id, 2, 3, ThirdReason);
        var v3 = await SupersedeAsync(billing, adminPath, v2Id, v3Meta, Digest(v3Preview), pdf3);
        var v3Id = v3.GetProperty("versionId").GetGuid();

        AssertVersionReceipt(v2, purchase, documentId, v1Id, 2, pdf2, SecondIssuer, SecondReference, SecondReason,
            P0611Tests.Instant.AddMinutes(2));
        AssertVersionReceipt(v3, purchase, documentId, v2Id, 3, pdf3, ThirdIssuer, ThirdReference, ThirdReason,
            P0611Tests.Instant.AddMinutes(4));

        await AssertAdminAndCustomerVersionsAsync(billing, purchase, adminPath, customerPath,
            [(v3Id, 3, v2Id, pdf3, ThirdIssuer, ThirdReference, ThirdReason),
             (v2Id, 2, v1Id, pdf2, SecondIssuer, SecondReference, SecondReason),
             (v1Id, 1, null, pdf1, P0611Tests.Issuer, P0611Tests.Reference, null)]);

        await AssertDatabaseLineageAsync(fixture, purchase, documentId,
            [(v1Id, 1, null, pdf1, null), (v2Id, 2, v1Id, pdf2, SecondReason), (v3Id, 3, v2Id, pdf3, ThirdReason)]);

        var confirmationAfter = await ReadConfirmationEvidenceAsync(purchase);
        Assert.Equal(confirmationBefore.Json, confirmationAfter.Json);
        Assert.Equal(confirmationBefore.Document, confirmationAfter.Document);
        Assert.Equal(fundingBefore, await FundingFactsAsync(fixture, purchase.Account, purchase.Quote.OrderId));
    }

    private static void AssertSupersedePreview(JsonElement preview, P0302Fixture.Purchase purchase,
        Guid documentId, Guid predecessorId, int predecessorVersion, int nextVersion, string reason)
    {
        Assert.Equal(1, preview.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(purchase.Account, preview.GetProperty("accountId").GetGuid());
        Assert.Equal(purchase.Quote.OrderId, preview.GetProperty("orderId").GetGuid());
        Assert.Equal(1, preview.GetProperty("paymentConfirmations").GetInt32());
        Assert.Equal(documentId, preview.GetProperty("documentId").GetGuid());
        Assert.Equal(predecessorId, preview.GetProperty("supersedesVersionId").GetGuid());
        Assert.Equal(predecessorVersion, preview.GetProperty("supersedesVersion").GetInt32());
        Assert.Equal(nextVersion, preview.GetProperty("version").GetInt32());
        Assert.Equal(reason, preview.GetProperty("reason").GetString());
    }

    private static void AssertVersionReceipt(JsonElement receipt, P0302Fixture.Purchase purchase, Guid documentId,
        Guid predecessorId, int version, byte[] bytes, string issuer, string reference, string reason,
        DateTimeOffset uploadedAt)
    {
        Assert.Equal(purchase.Account, receipt.GetProperty("accountId").GetGuid());
        Assert.Equal(purchase.Quote.OrderId, receipt.GetProperty("orderId").GetGuid());
        Assert.Equal(documentId, receipt.GetProperty("documentId").GetGuid());
        Assert.Equal(version, receipt.GetProperty("version").GetInt32());
        Assert.Equal(predecessorId, receipt.GetProperty("supersedesVersionId").GetGuid());
        Assert.Equal(issuer, receipt.GetProperty("issuerName").GetString());
        Assert.Equal(reference, receipt.GetProperty("issuerReference").GetString());
        Assert.Equal(reason, receipt.GetProperty("supersessionReason").GetString());
        Assert.Equal(bytes.Length, receipt.GetProperty("byteLength").GetInt32());
        Assert.Equal(P0611Tests.Hash(bytes), receipt.GetProperty("sha256").GetString());
        Assert.NotEqual(Guid.Empty, receipt.GetProperty("uploadedBy").GetGuid());
        Assert.Equal(uploadedAt, receipt.GetProperty("uploadedAt").GetDateTimeOffset());
        Assert.False(receipt.TryGetProperty("content", out _));
    }

    private static async Task AssertAdminAndCustomerVersionsAsync(P0110CookieBrowser billing,
        P0302Fixture.Purchase purchase, string adminPath, string customerPath,
        (Guid VersionId, int Version, Guid? PredecessorId, byte[] Bytes, string Issuer, string Ref, string? Reason)[] expected)
    {
        using var adminList = await billing.SendAsync(HttpMethod.Get, adminPath);
        var adminRows = (await ReadAsync(adminList, HttpStatusCode.OK)).GetProperty("documents").EnumerateArray().ToArray();
        Assert.Equal(expected.Length, adminRows.Length);

        using var memberList = await purchase.Payer.Client.GetAsync(customerPath);
        Assert.Equal(HttpStatusCode.OK, memberList.StatusCode);
        Assert.True(memberList.Headers.CacheControl?.NoStore);
        var memberRows = (await memberList.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("documents").EnumerateArray().ToArray();
        Assert.Equal(expected.Length, memberRows.Length);

        for (var i = 0; i < expected.Length; i++)
        {
            var item = expected[i];
            Assert.Equal(item.VersionId, adminRows[i].GetProperty("versionId").GetGuid());
            Assert.Equal(item.Version, adminRows[i].GetProperty("version").GetInt32());
            Assert.Equal(item.Reason, adminRows[i].GetProperty("supersessionReason").ValueKind == JsonValueKind.Null
                ? null : adminRows[i].GetProperty("supersessionReason").GetString());

            var row = memberRows[i];
            Assert.Equal(item.VersionId, row.GetProperty("versionId").GetGuid());
            Assert.Equal(item.Version, row.GetProperty("version").GetInt32());
            Assert.Equal(item.PredecessorId, row.GetProperty("supersedesVersionId").ValueKind == JsonValueKind.Null
                ? null : row.GetProperty("supersedesVersionId").GetGuid());
            Assert.Equal(item.Issuer, row.GetProperty("issuerName").GetString());
            Assert.Equal(item.Ref, row.GetProperty("issuerReference").GetString());
            Assert.Equal(P0611Tests.Hash(item.Bytes), row.GetProperty("sha256").GetString());
            Assert.False(row.TryGetProperty("supersessionReason", out _));
            Assert.False(row.TryGetProperty("reason", out _));

            using var download = await purchase.Payer.Client.GetAsync($"{customerPath}/{item.VersionId:D}/content");
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.True(download.Headers.CacheControl?.NoStore);
            var downloaded = await download.Content.ReadAsByteArrayAsync();
            Assert.Equal(item.Bytes, downloaded);
            Assert.Equal(P0611Tests.Hash(item.Bytes), P0611Tests.Hash(downloaded));
        }
    }

    private static async Task AssertDatabaseLineageAsync(P0302Fixture fixture, P0302Fixture.Purchase purchase,
        Guid documentId, (Guid VersionId, int Version, Guid? PredecessorId, byte[] Bytes, string? Reason)[] expected)
    {
        Assert.Equal(expected.Length, await fixture.CountAsync("billing_document_versions"));
        await using var conn = new NpgsqlConnection(fixture.Hosts.LicensingOwner.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT version_id, document_id, version_number, supersedes_version_id, content_sha256, content,
                supersession_reason, admin_id, session_id, mfa_verified_at
            FROM public.billing_document_versions
            WHERE account_id = $1 AND order_id = $2
            ORDER BY version_number ASC
            """, conn);
        cmd.Parameters.AddWithValue(purchase.Account);
        cmd.Parameters.AddWithValue(purchase.Quote.OrderId);
        var actual = new List<(Guid, Guid, int, Guid?, string, byte[], string?, Guid, Guid, DateTime)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            actual.Add((reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.GetString(4),
                (byte[])reader.GetValue(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetGuid(7), reader.GetGuid(8), reader.GetDateTime(9)));
        }
        Assert.Equal(expected.Length, actual.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].VersionId, actual[i].Item1);
            Assert.Equal(documentId, actual[i].Item2);
            Assert.Equal(expected[i].Version, actual[i].Item3);
            Assert.Equal(expected[i].PredecessorId, actual[i].Item4);
            Assert.Equal(P0611Tests.Hash(expected[i].Bytes), actual[i].Item5);
            Assert.Equal(expected[i].Bytes, actual[i].Item6);
            Assert.Equal(expected[i].Reason, actual[i].Item7);
            Assert.NotEqual(Guid.Empty, actual[i].Item8);
            Assert.NotEqual(Guid.Empty, actual[i].Item9);
        }
    }

    internal static async Task<(string Json, byte[] Document)> ReadConfirmationEvidenceAsync(P0302Fixture.Purchase purchase)
    {
        var path = P0302Fixture.PaymentPath(purchase.Quote) + "/confirmations";
        var json = await purchase.Payer.Client.GetStringAsync(path);
        using var doc = JsonDocument.Parse(json);
        var id = Assert.Single(doc.RootElement.EnumerateArray()).GetProperty("confirmationId").GetGuid();
        var bytes = await purchase.Payer.Client.GetByteArrayAsync($"{path}/{id:D}/document");
        return (json, bytes);
    }

    internal static async Task<string> FundingFactsAsync(P0302Fixture fixture, Guid account, Guid order) =>
        (string)(await P0611Tests.ScalarAsync(fixture, $"""
            SELECT concat_ws('|',
                (SELECT count(*) FROM public.verified_payments WHERE account_id = '{account:D}' AND order_id = '{order:D}'),
                (SELECT coalesce(sum(amount_paise), 0) FROM public.verified_payments WHERE account_id = '{account:D}' AND order_id = '{order:D}'),
                (SELECT count(*) FROM public.payment_fulfilments WHERE account_id = '{account:D}' AND order_id = '{order:D}'),
                (SELECT count(*) FROM public.payment_confirmations WHERE account_id = '{account:D}' AND order_id = '{order:D}'))
            """))!;

    internal static Dictionary<string, object> SupersedeMetadata(byte[] bytes, string fileName, string issuer,
        string reference, DateOnly issuedOn, string reason) => new()
    {
        ["kind"] = "AccountingInvoice",
        ["fileName"] = fileName,
        ["mediaType"] = "application/pdf",
        ["byteLength"] = bytes.Length,
        ["sha256"] = P0611Tests.Hash(bytes),
        ["issuerName"] = issuer,
        ["issuerReference"] = reference,
        ["issuedOn"] = issuedOn,
        ["reason"] = reason,
    };

    internal static Dictionary<string, object> SupersedeCommand(Dictionary<string, object> metadata, string digest,
        Guid? operationKey = null, bool confirmed = true) =>
        new(metadata)
        {
            ["operationKey"] = operationKey ?? Guid.NewGuid(),
            ["previewDigest"] = digest,
            ["confirmed"] = confirmed,
        };

    internal static string Digest(JsonElement preview) => preview.GetProperty("previewDigest").GetString()!;

    internal static async Task<JsonElement> PreviewSupersedeAsync(P0110CookieBrowser billing, string adminPath,
        Guid predecessor, object metadata)
    {
        using var response = await P0212Fixture.PostAsync(billing, $"{adminPath}/{predecessor:D}/supersede/preview", metadata);
        return await ReadAsync(response, HttpStatusCode.OK);
    }

    internal static async Task<JsonElement> SupersedeAsync(P0110CookieBrowser billing, string adminPath,
        Guid predecessor, Dictionary<string, object> metadata, string digest, byte[] bytes)
    {
        using var response = await SendSupersedeAsync(billing, adminPath, predecessor,
            SupersedeCommand(metadata, digest), bytes, (string)metadata["fileName"]);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return await ReadAsync(response, HttpStatusCode.Created);
    }

    internal static async Task<HttpResponseMessage> SendSupersedeAsync(P0110CookieBrowser billing, string adminPath,
        Guid predecessor, object command, byte[] bytes, string fileName = "fixture-invoice-v2.pdf",
        string mediaType = "application/pdf", string? proof = null, string origin = P0110CookieBrowser.Origin)
    {
        using var form = new MultipartFormDataContent();
        form.Add(JsonContent.Create(command), "command");
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        form.Add(content, "content", fileName);
        return await billing.SendAsync(HttpMethod.Post, $"{adminPath}/{predecessor:D}/supersede", form,
            proof is null ? await P0206Fixture.ProofAsync(billing) : proof.Length == 0 ? null : proof, origin);
    }

    internal static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"Expected {(int)expected}, got {(int)response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.Clone();
    }

    internal static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected, string code) =>
        Assert.Equal(code, (await ReadAsync(response, expected)).GetProperty("code").GetString());

    internal static byte[] DistinctPdf(string label)
    {
        var drawing = $"BT /F1 18 Tf 36 110 Td ({label}) Tj ET";
        string[] bodies =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 160] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {drawing.Length} >>\nstream\n{drawing}\nendstream",
        ];
        var pdf = new StringBuilder("%PDF-1.7\n");
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
