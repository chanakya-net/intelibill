using System.Diagnostics;
using System.Text.Json;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0418BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Config = "../../tests/licensing/browser/playwright.p0418.config.ts";

    [P0418BrowserFact]
    [Trait("Category", "Browser")]
    public async Task EnrolledMerchantJourneyFollowsRestrictionAndRelease()
    {
        var evidence = Path.Combine(P0418Tests.EvidenceDirectory(), "browser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        output.WriteLine("Browser evidence: {0}", evidence);
        var frontend = Path.Combine(ProjectPaths.Root, "src/frontend");
        using var staged = new MerchantWebBuild(frontend);
        await using var fixture = await P0213Fixture.StartAsync(database, browser: true, prepareProvider: true,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var (shop, account) = await P0418Tests.PrepareShopAsync(fixture);
        using var client = shop.Client;
        var (_, code) = await P0418Tests.AddServiceAsync(shop);
        using var admin = await P0403Tests.SignInSecurityAsync(fixture);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var ct = timeout.Token;
        await using var web = await StartMerchantWebAsync(staged.Root, client.BaseAddress!, ct);
        await using var browser = StartBrowser(frontend, evidence, web.Origin, shop.Email, code);
        var browserReport = CaptureBrowserAsync(browser.Process, evidence, ct);

        await WaitForPhaseAsync(evidence, "operating", browser.Process, browserReport, ct);
        var original = await DocumentsAsync(fixture, shop.ShopId);
        Assert.False(string.IsNullOrWhiteSpace(original));
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        var id = await P0404Tests.SuspendAsync(admin, path, "Shop", shop.ShopId, "Trading");
        await P0403Tests.AssertPropagationAsync(admin, path, id, "Pending");
        await P0418Tests.DrainAsync(fixture);
        await P0403Tests.AssertPropagationAsync(admin, path, id, "Acknowledged");
        await File.WriteAllTextAsync(Path.Combine(evidence, "restricted.ready"), "Acknowledged", ct);

        await WaitForPhaseAsync(evidence, "restricted", browser.Process, browserReport, ct);
        Assert.Equal(original, await DocumentsAsync(fixture, shop.ShopId));
        await P0418Tests.ReleaseAsync(admin, path, id);
        await P0418Tests.DrainAsync(fixture);
        await File.WriteAllTextAsync(Path.Combine(evidence, "released.ready"), "Applied", ct);

        var report = await browserReport;
        output.WriteLine(report);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal), report);
        await VerifyFinalFactsAsync(fixture, shop.ShopId, original!, evidence, ct);
    }

    private static OwnedProcess StartBrowser(string frontend, string evidence, string origin, string email, string serviceCode)
    {
        var start = new ProcessStartInfo("bun") { WorkingDirectory = frontend, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", Config })
            start.ArgumentList.Add(arg);
        start.Environment["NODE_PATH"] = Path.Combine(frontend, "node_modules");
        start.Environment["P0418_BROWSER_EVIDENCE"] = evidence;
        start.Environment["P0418_SERVICE_CODE"] = serviceCode;
        start.Environment["P0213_MERCHANT_WEB"] = origin;
        start.Environment["P0213_OWNER_EMAIL"] = email;
        start.Environment["P0213_MERCHANT_PASSWORD"] = P0201Tests.Password;
        return new OwnedProcess(start);
    }

    private static async Task<string> CaptureBrowserAsync(Process process, string evidence, CancellationToken ct)
    {
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var report = (await stdout) + (await stderr);
        await File.WriteAllTextAsync(Path.Combine(evidence, "playwright.log"), report, ct);
        return report;
    }

    private static async Task WaitForPhaseAsync(string evidence, string phase, Process browser, Task<string> report,
        CancellationToken ct)
    {
        while (!File.Exists(Path.Combine(evidence, $"observations-{phase}.json")))
        {
            if (browser.HasExited) Assert.Fail(await report);
            await Task.Delay(50, ct);
        }
    }

    private static async Task<string?> DocumentsAsync(P0213Fixture fixture, Guid shop) =>
        (string?)await fixture.MerchantScalarAsync($"""
            SELECT string_agg(invoice_number || ':' || md5(invoice_document::text), ',' ORDER BY invoice_number)
            FROM sales WHERE shop_id = '{shop:D}'
            """);

    private static async Task VerifyFinalFactsAsync(P0213Fixture fixture, Guid shop, string original, string evidence,
        CancellationToken ct)
    {
        var docs = await DocumentsAsync(fixture, shop);
        Assert.NotNull(docs);
        Assert.StartsWith(original + ",", docs);
        var sales = (long)(await fixture.MerchantScalarAsync($"SELECT count(*) FROM sales WHERE shop_id = '{shop:D}'"))!;
        Assert.Equal(2L, sales);
        var enrolled = await fixture.MerchantScalarAsync("SELECT count(*) FROM shop_license_states WHERE enforcement_activated_at IS NOT NULL");
        Assert.Equal(1L, enrolled);
        await File.WriteAllTextAsync(Path.Combine(evidence, "postgres-evidence.json"), JsonSerializer.Serialize(new
        {
            provenance = "Chromium, shipping merchant build, loopback Kestrel merchant and licensing APIs, PostgreSQL, fixture keys",
            sales, firstDocumentUnchanged = true, documents = docs, enrolled, restriction = "Acknowledged", release = "Applied",
        }), ct);
    }
}

internal sealed class P0418BrowserFactAttribute : FactAttribute
{
    public P0418BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0418_RUN_BROWSER") != "1")
            Skip = "Set P0418_RUN_BROWSER=1 after building the shipping merchant application.";
    }
}
