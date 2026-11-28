using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0607aTests;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Dual-host shipping admin + merchant web builds in Chromium against real APIs and real PostgreSQL.
/// Development simulator payment and local PDF fixtures only; no live provider verification.
/// </summary>
public sealed class P0613BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Config = "../../../tests/backend/licensing/browser/playwright.p0613.config.ts";
    private const string FinanceEmail = "p0613.browser.finance@example.test";
    private const string TransferManagerEmail = "p0613.browser.transfer@example.test";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [P0613BrowserFact]
    [Trait("Category", "Browser")]
    public async Task FinanceCorrectsInvoiceAndMerchantDownloadsBothVersions()
    {
        var evidence = Environment.GetEnvironmentVariable("P0613_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence), "Set P0613_EVIDENCE_DIR.");
        Directory.CreateDirectory(evidence);
        var scratch = Directory.CreateTempSubdirectory("intelibill-p0613-");
        using var rsa = TestRsaKeys.Create(TestRsaKeyPurpose.General);
        await using var s = await StartAsync(database, rsa, browser: true);
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        try { await RunAsync(s, evidence, scratch.FullName, limit.Token); }
        finally { scratch.Delete(recursive: true); }
    }

    private async Task RunAsync(Scenario s, string evidence, string scratch, CancellationToken ct)
    {
        var doc = await P0607Tests.AttachOriginalAsync(s);
        var v1Pdf = P0611Tests.Pdf();
        var v2Pdf = P0613Tests.DistinctPdf("Browser corrective accounting invoice v2");
        await File.WriteAllBytesAsync(Path.Combine(scratch, "fixture-invoice-v2.pdf"), v2Pdf, ct);

        string financeSeed, managerSeed;
        using (var owner = await s.F.Checkout.Trial.Catalogue.SignInOwnerAsync())
        {
            financeSeed = await P0611Tests.EnrollAsync(s.F.Checkout, owner, FinanceEmail, "billing");
            managerSeed = await P0611Tests.EnrollAsync(s.F.Checkout, owner, TransferManagerEmail, "licensing-manager");
        }
        s.F.Checkout.Trial.Catalogue.Clock.Advance(TimeSpan.FromSeconds(30));

        var merchantRoot = Path.Combine(ProjectPaths.Root, "src/frontend/intelibill");
        var adminRoot = Path.Combine(ProjectPaths.Root, "src/frontend/licensing");
        using var staged = new MerchantWebBuild(merchantRoot);
        await using var merchantApi = await P0310ApiRelay.StartAsync(s.Source.Payer.Client.BaseAddress!, ct);
        await using var adminApi = await P0310ApiRelay.StartAsync(s.F.Checkout.Delivery.LicensingApi.BaseAddress!, ct);
        await using var merchant = await StartMerchantWebAsync(staged.Root, merchantApi.Origin, ct);
        await using var admin = await StartAdminWebAsync(adminRoot, adminApi.Origin, ct);
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(s.F.Checkout, admin.Origin, ct);
        await WriteFixtureAsync(s, doc, v1Pdf, v2Pdf, financeSeed, scratch, ct);

        var start = BrowserStart(adminRoot, admin.Origin, merchant.Origin, scratch, evidence);
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await ApplyTransferAfterCorrectionAsync(browser.Process, s, managerSeed, scratch, ct);
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout + await stderr;
        output.WriteLine(report);
        await File.WriteAllTextAsync(Path.Combine(evidence, "playwright.log"), report + $"\nexit={browser.Process.ExitCode}\n", ct);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal)
            && !report.Contains("skipped", StringComparison.OrdinalIgnoreCase), report);
        await RecordDatabaseEvidenceAsync(s, doc, v1Pdf, v2Pdf, evidence, adminRoot, merchantRoot, ct);
    }

    private static async Task ApplyTransferAfterCorrectionAsync(Process process, Scenario s, string managerSeed,
        string scratch, CancellationToken ct)
    {
        var committed = Path.Combine(scratch, "v2-committed");
        while (!process.HasExited && !File.Exists(committed)) await Task.Delay(50, ct);
        if (process.HasExited) return;
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(s.F.Checkout, P0110CookieBrowser.Origin, ct);
        using var manager = await s.F.Checkout.Trial.Catalogue.SignInOperatorAsync(managerSeed, TransferManagerEmail);
        await P0607Tests.ApplyTransferAsync(s, manager);
        await File.WriteAllTextAsync(Path.Combine(scratch, "transfer-applied"), "applied", ct);
    }

    private static async Task WriteFixtureAsync(Scenario s, P0607Tests.OriginalDocument doc, byte[] v1Pdf, byte[] v2Pdf,
        string financeSeed, string scratch, CancellationToken ct)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var file = new FileStream(Path.Combine(scratch, "fixture.json"), options);
        await JsonSerializer.SerializeAsync(file, new
        {
            financeEmail = FinanceEmail,
            financePassword = P0114Tests.InviteePassword,
            financeSeed,
            financeTime = s.F.Hosts.Fixture.Clock.GetUtcNow().ToUnixTimeSeconds(),
            sourcePayerEmail = s.Source.Payer.Email,
            destPayerEmail = s.Destination.Payer.Email,
            merchantPassword = P0201Tests.Password,
            sourceAccount = s.Source.Account,
            destAccount = s.Destination.Account,
            order = doc.Order,
            v1VersionId = doc.Version,
            v1Sha256 = P0611Tests.Hash(v1Pdf),
            v2Sha256 = P0611Tests.Hash(v2Pdf),
        }, cancellationToken: ct);
    }

    private static ProcessStartInfo BrowserStart(string adminRoot, string adminOrigin, string merchantOrigin,
        string scratch, string evidence)
    {
        var start = new ProcessStartInfo("bun")
        {
            WorkingDirectory = adminRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", Config })
            start.ArgumentList.Add(arg);
        start.Environment["P0613_ADMIN_WEB"] = adminOrigin;
        start.Environment["P0613_MERCHANT_WEB"] = merchantOrigin;
        start.Environment["P0613_TEMPORARY"] = scratch;
        start.Environment["P0613_EVIDENCE_DIR"] = evidence;
        start.Environment["NODE_PATH"] = Path.Combine(adminRoot, "node_modules");
        return start;
    }

    private sealed record StoredDocumentRow(
        int Version, Guid VersionId, Guid? Predecessor, string Hash, byte[] Bytes, string? Reason);

    private static async Task RecordDatabaseEvidenceAsync(Scenario s, P0607Tests.OriginalDocument doc, byte[] v1Pdf,
        byte[] v2Pdf, string evidence, string adminRoot, string merchantRoot, CancellationToken ct)
    {
        var v1Hash = P0611Tests.Hash(v1Pdf);
        var v2Hash = P0611Tests.Hash(v2Pdf);
        Assert.Equal(v1Pdf, await File.ReadAllBytesAsync(Path.Combine(evidence, "merchant-invoice-v1.pdf"), ct));
        Assert.Equal(v2Pdf, await File.ReadAllBytesAsync(Path.Combine(evidence, "merchant-invoice-v2.pdf"), ct));

        var rows = await ReadStoredDocumentRowsAsync(s.F.Hosts.LicensingOwner.ConnectionString, s.Source.Account, doc.Order, ct);
        AssertStoredVersionsMatch(rows, doc.Version, v1Pdf, v1Hash, v2Pdf, v2Hash);

        var active = await s.F.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT account_id FROM public.shop_license_states WHERE shop_id='{s.North.ShopId:D}'");
        Assert.Equal(s.Destination.Account, active);
        Assert.Equal(1L, await s.F.CountAsync("payment_confirmations",
            $"account_id = '{s.Source.Account:D}' AND order_id = '{doc.Order:D}'"));

        await WriteBrowserPostgresEvidenceAsync(s.Source.Account, active!, doc.Order, rows, v1Pdf.Length, v1Hash,
            v2Pdf.Length, v2Hash, evidence, adminRoot, merchantRoot, ct);
    }

    private static async Task<List<StoredDocumentRow>> ReadStoredDocumentRowsAsync(
        string connectionString, Guid accountId, Guid orderId, CancellationToken ct)
    {
        var rows = new List<StoredDocumentRow>();
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT version_number, version_id, supersedes_version_id, content_sha256, content, supersession_reason
            FROM public.billing_document_versions
            WHERE account_id = $1 AND order_id = $2
            ORDER BY version_number ASC
            """, conn);
        cmd.Parameters.AddWithValue(accountId);
        cmd.Parameters.AddWithValue(orderId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            rows.Add(new(r.GetInt32(0), r.GetGuid(1), r.IsDBNull(2) ? null : r.GetGuid(2),
                r.GetString(3), (byte[])r.GetValue(4), r.IsDBNull(5) ? null : r.GetString(5)));
        }
        return rows;
    }

    private static void AssertStoredVersionsMatch(List<StoredDocumentRow> rows, Guid v1VersionId,
        byte[] v1Pdf, string v1Hash, byte[] v2Pdf, string v2Hash)
    {
        Assert.Equal(2, rows.Count);
        Assert.Equal((1, v1VersionId, (Guid?)null, v1Hash, (string?)null),
            (rows[0].Version, rows[0].VersionId, rows[0].Predecessor, rows[0].Hash, rows[0].Reason));
        Assert.Equal(v1Pdf, rows[0].Bytes);
        Assert.Equal((2, (Guid?)v1VersionId, v2Hash), (rows[1].Version, rows[1].Predecessor, rows[1].Hash));
        Assert.Equal(v2Pdf, rows[1].Bytes);
        Assert.False(string.IsNullOrWhiteSpace(rows[1].Reason));
    }

    private static async Task WriteBrowserPostgresEvidenceAsync(Guid sourceAccount, object activeAccount, Guid orderId,
        List<StoredDocumentRow> rows, int v1Length, string v1Hash, int v2Length, string v2Hash,
        string evidence, string adminRoot, string merchantRoot, CancellationToken ct)
    {
        using var ngsw = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(merchantRoot, "dist/INVENTORY/browser/ngsw.json"), ct));
        var merchantBuildId = ngsw.RootElement.GetProperty("appData").GetProperty("buildId").GetString();
        var adminIndexSha256 = P0611Tests.Hash(await File.ReadAllBytesAsync(
            Path.Combine(adminRoot, "dist/ADMIN/browser/index.html"), ct));
        var merchantIndexSha256 = P0611Tests.Hash(await File.ReadAllBytesAsync(
            Path.Combine(merchantRoot, "dist/INVENTORY/browser/index.html"), ct));

        await File.WriteAllTextAsync(Path.Combine(evidence, "browser-postgres.json"), JsonSerializer.Serialize(new
        {
            boundary = "Shipping admin and merchant web builds, real merchant and licensing APIs, real PostgreSQL, development simulator payment and local PDF fixtures only. No live provider verification.",
            sourceAccount,
            destinationAccount = activeAccount,
            orderId,
            documentVersions = rows.Count,
            paymentConfirmations = 1,
            v1 = new { versionId = rows[0].VersionId, sha256 = v1Hash, byteLength = v1Length, bytesMatch = true },
            v2 = new { versionId = rows[1].VersionId, supersedesVersionId = rows[1].Predecessor, sha256 = v2Hash, byteLength = v2Length, bytesMatch = true },
            adminIndexSha256,
            merchantBuildId,
            merchantIndexSha256,
            command = "bun x playwright test --config " + Config,
            passed = 1,
            skipped = 0,
        }, Indented), ct);
    }
}

internal sealed class P0613BrowserFactAttribute : FactAttribute
{
    public P0613BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0613_RUN_BROWSER") != "1")
            Skip = "Set P0613_RUN_BROWSER=1 after building both shipping web apps.";
    }
}
