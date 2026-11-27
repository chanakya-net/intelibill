using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Shipping merchant web + admin web builds in Chromium against real merchant and licensing APIs on real PostgreSQL.
/// Development simulator receipt fixture only; no live payment provider is contacted.
/// </summary>
public sealed class P0608BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Configuration = "../../tests/backend/licensing/browser/playwright.p0608.config.ts";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [P0608BrowserFact]
    [Trait("Category", "Browser")]
    public async Task MemberRequestsRefundAndOperatorSeesIntakeInChromium()
    {
        var evidence = Environment.GetEnvironmentVariable("P0608_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence), "Set P0608_EVIDENCE_DIR.");
        Directory.CreateDirectory(evidence);
        var scratch = Directory.CreateTempSubdirectory("intelibill-p0608-");
        try { await RunAsync(evidence, scratch.FullName); }
        finally { scratch.Delete(recursive: true); }
    }

    private async Task RunAsync(string evidence, string scratch)
    {
        await using var fixture = await StartAsync(database, P0608Tests.RequestedInstant, browser: true);
        var package = await fixture.PackageAsync(2, P0608Tests.ShopNames);
        var (purchase, term) = await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(fixture);
        var confirmation = await P0608Tests.ConfirmationIdAsync(package.Payer.Client, package.Account, purchase.OrderId);
        var before = await P0608Tests.SnapshotAsync(fixture, package, term);
        var seed = await P0306Tests.EnrollBillingAsync(fixture.Checkout, P0306Tests.BillingEmail);

        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var ct = deadline.Token;
        var merchantRoot = Path.Combine(ProjectPaths.Root, "src/frontend");
        var adminRoot = Path.Combine(ProjectPaths.Root, "src/admin-frontend");
        using var staged = new MerchantWebBuild(merchantRoot);
        await using var merchantApi = await P0310ApiRelay.StartAsync(package.Payer.Client.BaseAddress!, ct);
        await using var adminApi = await P0310ApiRelay.StartAsync(fixture.Checkout.Delivery.LicensingApi.BaseAddress!, ct);
        await using var merchant = await StartMerchantWebAsync(staged.Root, merchantApi.Origin, ct);
        await using var admin = await StartAdminWebAsync(adminRoot, adminApi.Origin, ct);
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(fixture.Checkout, admin.Origin, ct);
        await WriteSecretsAsync(scratch, fixture, package, seed, ct);

        var start = BrowserStart(adminRoot, admin.Origin, merchant.Origin, scratch, evidence,
            package.Account, purchase.OrderId, confirmation);
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout + await stderr;
        output.WriteLine(report);
        await File.WriteAllTextAsync(Path.Combine(evidence, "playwright.log"), report + $"\nexit={browser.Process.ExitCode}\n", ct);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal)
            && !report.Contains("skipped", StringComparison.OrdinalIgnoreCase), report);

        var after = await P0608Tests.SnapshotAsync(fixture, package, term);
        Assert.Equal(before, after);
        Assert.Equal(1L, await fixture.CountAsync("refund_requests"));
        await RecordAsync(fixture, evidence, merchantRoot, adminRoot, before, after, ct);
    }

    private static async Task WriteSecretsAsync(string scratch, P0308Fixture fixture, Package package,
        string seed, CancellationToken ct)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var file = new FileStream(Path.Combine(scratch, "sessions.json"), options);
        await JsonSerializer.SerializeAsync(file, new
        {
            admin = P0306Tests.BillingEmail,
            adminPassword = P0114Tests.InviteePassword,
            seed,
            adminTime = fixture.Hosts.Fixture.Clock.GetUtcNow().ToUnixTimeSeconds(),
            merchant = package.Payer.Email,
            merchantPassword = P0201Tests.Password,
        }, cancellationToken: ct);
    }

    private static ProcessStartInfo BrowserStart(string root, string adminOrigin, string merchantOrigin,
        string scratch, string evidence, Guid account, Guid order, Guid confirmation)
    {
        var start = new ProcessStartInfo("bun")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", Configuration })
            start.ArgumentList.Add(arg);
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["ADMIN_WEB"] = adminOrigin,
            ["MERCHANT_WEB"] = merchantOrigin,
            ["TEMPORARY"] = scratch,
            ["EVIDENCE_DIR"] = evidence,
            ["ACCOUNT"] = account.ToString("D"),
            ["ORDER"] = order.ToString("D"),
            ["CONFIRMATION"] = confirmation.ToString("D"),
        }) start.Environment["P0608_" + key] = value;
        start.Environment["NODE_PATH"] = Path.Combine(root, "node_modules");
        return start;
    }

    private static async Task RecordAsync(P0308Fixture fixture, string evidence, string merchantRoot,
        string adminRoot, string before, string after, CancellationToken ct)
    {
        var refundId = (await File.ReadAllTextAsync(Path.Combine(evidence, "refund-request-id.txt"), ct)).Trim();
        using var ngsw = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(merchantRoot, "dist/INVENTORY/browser/ngsw.json"), ct));
        var adminIndexSha = Convert.ToHexStringLower(SHA256.HashData(
            await File.ReadAllBytesAsync(Path.Combine(adminRoot, "dist/ADMIN/browser/index.html"), ct)));
        await File.WriteAllTextAsync(Path.Combine(evidence, "build-and-postgres.json"), JsonSerializer.Serialize(new
        {
            refundRequestId = refundId,
            refundRequests = await fixture.CountAsync("refund_requests"),
            commercialFactsEqualBeforeAndAfter = before == after,
            merchantBuildId = ngsw.RootElement.GetProperty("appData").GetProperty("buildId").GetString(),
            adminIndexSha,
            command = "bun x playwright test --config " + Configuration,
            passed = 1,
            skipped = 0,
            boundary = "Development simulator receipt fixture and shipping local merchant/admin builds only; no live payment provider verification.",
        }, Indented), ct);
    }
}

internal sealed class P0608BrowserFactAttribute : FactAttribute
{
    public P0608BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0608_RUN_BROWSER") != "1")
            Skip = "Set P0608_RUN_BROWSER=1 after building both current apps.";
    }
}
