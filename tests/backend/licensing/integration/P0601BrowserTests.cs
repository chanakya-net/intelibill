using System.Diagnostics;
using System.Text.Json;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Shipping merchant web build in Chromium against real merchant and licensing APIs (loopback Kestrel), each on its
/// own PostgreSQL database. A three-slot monthly package bought on 31 January 2028 (leap year) is quoted for a fourth
/// slot on 14 February after a catalogue reprice. Simulator purchase evidence only: no real provider is contacted.
/// </summary>
public sealed class P0601BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Configuration = "../../tests/backend/licensing/browser/playwright.p0601.config.ts";

    [P0601BrowserFact]
    [Trait("Category", "Browser")]
    public async Task OwnerReviewsCapacityIncreaseInChromium()
    {
        var evidence = Environment.GetEnvironmentVariable("P0601_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence), "Set P0601_EVIDENCE_DIR.");
        Directory.CreateDirectory(evidence);
        await using var fixture = await StartAsync(database, P0601Tests.January31, browser: true);
        var package = await fixture.PackageAsync(2, P0601Tests.ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");
        await fixture.Checkout.Delivery.DrainAsync(source: true);
        await fixture.Checkout.Delivery.DrainAsync(source: false);
        var successor = await P0601Tests.RepriceAsync(fixture, package, 18000L);
        var before = await P0601Tests.EvidenceAsync(fixture, term);
        fixture.At(P0601Tests.February14);

        var frontend = Path.Combine(ProjectPaths.Root, "src/frontend");
        using var staged = new MerchantWebBuild(frontend);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        await using var web = await StartMerchantWebAsync(staged.Root, package.Payer.Client.BaseAddress!, timeout.Token);
        var run = new Run(frontend, evidence, package.Payer.Email, package.Account, term, successor);
        await RunAsync(run, web.Origin, timeout.Token);

        Assert.Equal(before, await P0601Tests.EvidenceAsync(fixture, term));
        Assert.Equal(1L, await fixture.CountAsync("capacity_change_quotes"));
        await RecordAsync(fixture, run, before, timeout.Token);
    }

    private async Task RunAsync(Run run, string origin, CancellationToken ct)
    {
        var start = new ProcessStartInfo("bun") { WorkingDirectory = run.Frontend, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", Configuration, "--grep", "@quote" })
            start.ArgumentList.Add(arg);
        start.Environment["NODE_PATH"] = Path.Combine(run.Frontend, "node_modules");
        start.Environment["P0601_MERCHANT_WEB"] = origin;
        start.Environment["P0601_OWNER"] = run.Owner;
        start.Environment["P0601_PASSWORD"] = P0201Tests.Password;
        start.Environment["P0601_EVIDENCE_DIR"] = run.Evidence;
        start.Environment["P0601_ACCOUNT"] = run.Account.ToString("D");
        start.Environment["P0601_TERM"] = run.Term.ToString("D");
        start.Environment["P0601_TARGET"] = run.Target.ToString("D");
        start.Environment["P0601_AMOUNT"] = P0601Tests.Rupees(10862L);
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout;
        output.WriteLine(report);
        var raw = Path.Combine(run.Evidence, "playwright");
        if (Directory.Exists(raw)) Directory.Delete(raw, recursive: true);
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "quote-playwright.log"), report, ct);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal)
            && !report.Contains("skipped", StringComparison.OrdinalIgnoreCase), report + await stderr);
    }

    private static async Task RecordAsync(P0308Fixture fixture, Run run, string before, CancellationToken ct)
    {
        using var ngsw = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(run.Frontend, "dist/INVENTORY/browser/ngsw.json"), ct));
        var git = new ProcessStartInfo("git") { WorkingDirectory = ProjectPaths.Root, RedirectStandardOutput = true };
        git.ArgumentList.Add("rev-parse");
        git.ArgumentList.Add("HEAD");
        await using var process = new OwnedProcess(git);
        var sha = (await process.Process.StandardOutput.ReadToEndAsync(ct)).Trim();
        await process.Process.WaitForExitAsync(ct);
        var quote = (await File.ReadAllTextAsync(Path.Combine(run.Evidence, "quote.txt"), ct)).Trim();
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "build-and-postgres.json"), JsonSerializer.Serialize(new
        {
            sha, buildId = ngsw.RootElement.GetProperty("appData").GetProperty("buildId").GetString(), quote,
            term = run.Term, termAndPaymentEvidenceBefore = before,
            termAndPaymentEvidenceAfter = await P0601Tests.EvidenceAsync(fixture, run.Term),
            storedQuote = await fixture.ScalarAsync($"""
                SELECT outcome || ' amount=' || amount_paise || ' purchased=' || purchased_gross_paise || ' target=' ||
                    target_gross_paise || ' seconds=' || remaining_seconds || '/' || total_seconds || ' shops=' || shop_count ||
                    ' provider=' || provider_id || ' routing=' || routing_revision
                FROM capacity_change_quotes WHERE quote_id = '{quote}'
                """),
            capacityQuotes = await fixture.CountAsync("capacity_change_quotes"),
            checkoutOrders = await fixture.CountAsync("checkout_orders"),
            paymentAttempts = await fixture.CountAsync("payment_attempts"),
            verifiedPayments = await fixture.CountAsync("verified_payments"),
            fulfilments = await fixture.CountAsync("payment_fulfilments"),
            command = "bun x playwright test --config " + Configuration + " --grep @quote", passed = 1, skipped = 0,
            boundary = "Development simulator purchase and local catalogue fixtures only; no real payment provider was contacted.",
        }), ct);
    }

    private sealed record Run(string Frontend, string Evidence, string Owner, Guid Account, Guid Term, Guid Target);
}

internal sealed class P0601BrowserFactAttribute : FactAttribute
{
    public P0601BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0601_RUN_BROWSER") != "1")
            Skip = "Set P0601_RUN_BROWSER=1 after building the shipping merchant application.";
    }
}
