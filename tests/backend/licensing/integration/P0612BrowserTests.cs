using System.Diagnostics;
using System.Text.Json;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Shipping merchant web build in Chromium against real merchant and licensing APIs on real PostgreSQL.
/// Development simulator payment and local PDF fixture only; no live provider verification.
/// </summary>
public sealed class P0612BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Config = "../../../tests/backend/licensing/browser/playwright.p0612.config.ts";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [P0612BrowserFact]
    [Trait("Category", "Browser")]
    public async Task MemberDownloadsPrivateInvoiceThroughShippingMerchantWeb()
    {
        var evidence = Environment.GetEnvironmentVariable("P0612_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence), "Set P0612_EVIDENCE_DIR.");
        Directory.CreateDirectory(evidence);
        var scratch = Directory.CreateTempSubdirectory("intelibill-p0612-");
        try { await RunAsync(evidence, scratch.FullName); }
        finally { scratch.Delete(recursive: true); }
    }

    private async Task RunAsync(string evidence, string scratch)
    {
        await using var fixture = await P0611Tests.StartAsync(database, browser: true);
        var purchase = await P0311Tests.VerifiedAsync(fixture);
        var other = await P0209Fixture.CreateAccountAsync(purchase.Payer.Client, "Foreign account");
        using var billing = await P0611Tests.SignInBillingAsync(fixture);
        var pdf = P0611Tests.Pdf();
        var uploaded = await P0611Tests.AttachAsync(billing, P0611Tests.Path(purchase.Quote), pdf);
        var versionId = uploaded.GetProperty("versionId").GetGuid();
        var expectedSha256 = P0611Tests.Hash(pdf);

        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var ct = deadline.Token;
        var merchantRoot = Path.Combine(ProjectPaths.Root, "src/frontend/intelibill");
        using var staged = new MerchantWebBuild(merchantRoot);
        await using var merchantApi = await P0310ApiRelay.StartAsync(purchase.Payer.Client.BaseAddress!, ct);
        await using var merchant = await StartMerchantWebAsync(staged.Root, merchantApi.Origin, ct);
        await WriteSecretsAsync(scratch, purchase, ct);

        var start = BrowserStart(merchantRoot, merchant.Origin, scratch, evidence, purchase, other, versionId, expectedSha256);
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await MutateAfterDownloadAsync(browser.Process, fixture, purchase, scratch, ct);
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout + await stderr;
        output.WriteLine(report);
        await File.WriteAllTextAsync(Path.Combine(evidence, "playwright.log"), report + $"\nexit={browser.Process.ExitCode}\n", ct);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal)
            && !report.Contains("skipped", StringComparison.OrdinalIgnoreCase), report);
        await RecordAsync(fixture, evidence, merchantRoot, pdf, expectedSha256, versionId, ct);
    }

    private static async Task MutateAfterDownloadAsync(Process process, P0302Fixture fixture, Purchase purchase,
        string scratch, CancellationToken ct)
    {
        while (!process.HasExited && !File.Exists(Path.Combine(scratch, "downloaded")))
            await Task.Delay(50, ct);
        if (process.HasExited) return;
        await P0311Tests.MutateAsync(fixture, purchase);
        await File.WriteAllTextAsync(Path.Combine(scratch, "mutated"), "done", ct);
    }

    private static async Task WriteSecretsAsync(string scratch, Purchase purchase, CancellationToken ct)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var file = new FileStream(Path.Combine(scratch, "sessions.json"), options);
        await JsonSerializer.SerializeAsync(file, new
        {
            merchant = purchase.Payer.Email,
            merchantPassword = P0201Tests.Password,
        }, cancellationToken: ct);
    }

    private static ProcessStartInfo BrowserStart(string root, string merchantOrigin, string scratch, string evidence,
        Purchase purchase, Guid other, Guid versionId, string expectedSha256)
    {
        var start = new ProcessStartInfo("bun")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", Config })
            start.ArgumentList.Add(arg);
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["MERCHANT_WEB"] = merchantOrigin,
            ["TEMPORARY"] = scratch,
            ["EVIDENCE_DIR"] = evidence,
            ["ACCOUNT"] = purchase.Account.ToString("D"),
            ["ORDER"] = purchase.Quote.OrderId.ToString("D"),
            ["OTHER_ACCOUNT"] = other.ToString("D"),
            ["VERSION_ID"] = versionId.ToString("D"),
            ["EXPECTED_SHA256"] = expectedSha256,
        }) start.Environment["P0612_" + key] = value;
        start.Environment["NODE_PATH"] = Path.Combine(root, "node_modules");
        return start;
    }

    private static async Task RecordAsync(P0302Fixture fixture, string evidence, string merchantRoot, byte[] pdf,
        string expectedSha256, Guid versionId, CancellationToken ct)
    {
        Assert.Equal(1L, await fixture.CountAsync("billing_document_versions"));
        Assert.Equal(1L, await fixture.CountAsync("payment_confirmations"));
        Assert.Equal(expectedSha256, await P0611Tests.ScalarAsync(fixture, "SELECT content_sha256 FROM billing_document_versions"));
        Assert.Equal(pdf, await File.ReadAllBytesAsync(Path.Combine(evidence, "merchant-invoice-original.pdf"), ct));
        Assert.Equal(pdf, await File.ReadAllBytesAsync(Path.Combine(evidence, "merchant-invoice-after-edits.pdf"), ct));
        using var ngsw = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(merchantRoot, "dist/INVENTORY/browser/ngsw.json"), ct));
        var buildId = ngsw.RootElement.GetProperty("appData").GetProperty("buildId").GetString();
        await File.WriteAllTextAsync(Path.Combine(evidence, "browser-postgres.json"), JsonSerializer.Serialize(new
        {
            documentVersions = 1,
            paymentConfirmations = 1,
            versionId,
            sha256 = expectedSha256,
            byteLength = pdf.Length,
            bytesMatchStoredFixture = true,
            merchantBuildId = buildId,
            command = "bun x playwright test --config " + Config,
            passed = 1,
            skipped = 0,
            boundary = "Current merchant web build, real merchant and licensing APIs, real PostgreSQL, development simulator payment and local PDF fixture only. No live provider verification.",
        }, Indented), ct);
    }
}

internal sealed class P0612BrowserFactAttribute : FactAttribute
{
    public P0612BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0612_RUN_BROWSER") != "1")
            Skip = "Set P0612_RUN_BROWSER=1 after building the current merchant web app.";
    }
}
