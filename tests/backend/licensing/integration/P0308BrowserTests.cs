using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Shipping merchant web build in Chromium against real merchant and licensing APIs (loopback Kestrel), each on its
/// own PostgreSQL database. A paid package renewed inside grace: the merchant reviews previous paid-through, the
/// anchored new end and the included grace days, accepts, pays through the local simulator and sees access applied.
/// Simulator evidence only: no real provider is contacted.
/// </summary>
public sealed class P0308BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Configuration = "../../../tests/backend/licensing/browser/playwright.p0308.config.ts";
    private static readonly string[] ShopNames = ["Renewal Central", "Renewal East"];
    private static readonly string[] Phases = ["renew", "applied"];

    [P0308BrowserFact]
    [Trait("Category", "Browser")]
    public async Task BillingOwnerRenewsInGraceFromTheOriginalAnchor()
    {
        var evidence = Environment.GetEnvironmentVariable("P0308_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence), "Set P0308_EVIDENCE_DIR.");
        Directory.CreateDirectory(evidence);
        await using var fixture = await StartAsync(database, P0308Tests.March31, browser: true);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (first, term) = await PurchaseAsync(package, "Month");
        await fixture.Checkout.Delivery.DrainAsync(source: true);
        await fixture.Checkout.Delivery.DrainAsync(source: false);
        // 2 May 2027, 13:00 IST: two days and three hours into grace after the 30 April 10:00 IST paid-through.
        fixture.At(P0308Tests.April30.AddDays(2).AddHours(3));

        var run = new Run(Path.Combine(ProjectPaths.Root, "src/frontend/intelibill"), evidence, package.Payer.Email, package.Account, term,
            first.AmountPaise);
        using var staged = new MerchantWebBuild(run.Frontend);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        await using var web = await StartMerchantWebAsync(staged.Root, package.Payer.Client.BaseAddress!, timeout.Token);

        await RunAsync(run, web.Origin, "renew", timeout.Token);
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments", $"predecessor_term_id = '{term:D}'"));
        var pending = await fixture.CountAsync("entitlement_outbox", "status IN ('Pending', 'Leased')");
        await fixture.Checkout.Delivery.DrainAsync(source: true);
        await fixture.Checkout.Delivery.DrainAsync(source: false);
        await RunAsync(run, web.Origin, "applied", timeout.Token);
        await RecordAsync(fixture, run, pending, timeout.Token);
    }

    private async Task RunAsync(Run run, string origin, string phase, CancellationToken ct)
    {
        var start = new ProcessStartInfo("bun") { WorkingDirectory = run.Frontend, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", Configuration, "--grep", "@" + phase })
            start.ArgumentList.Add(arg);
        start.Environment["NODE_PATH"] = Path.Combine(run.Frontend, "node_modules");
        start.Environment["P0308_MERCHANT_WEB"] = origin;
        start.Environment["P0308_OWNER"] = run.Owner;
        start.Environment["P0308_PASSWORD"] = P0201Tests.Password;
        start.Environment["P0308_EVIDENCE_DIR"] = run.Evidence;
        start.Environment["P0308_ACCOUNT"] = run.Account.ToString("D");
        start.Environment["P0308_TERM"] = run.Term.ToString("D");
        start.Environment["P0308_TOTAL"] = "₹" + (run.AmountPaise / 100m).ToString("N2", CultureInfo.GetCultureInfo("en-IN"));
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout;
        output.WriteLine(report);
        var raw = Path.Combine(run.Evidence, "playwright");
        if (Directory.Exists(raw)) Directory.Delete(raw, recursive: true);
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, phase + "-playwright.log"), report, ct);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal)
            && !report.Contains("skipped", StringComparison.OrdinalIgnoreCase), report + await stderr);
    }

    private static async Task RecordAsync(P0308Fixture fixture, Run run, long pendingSnapshots, CancellationToken ct)
    {
        using var ngsw = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(run.Frontend, "dist/INVENTORY/browser/ngsw.json"), ct));
        var git = new ProcessStartInfo("git") { WorkingDirectory = ProjectPaths.Root, RedirectStandardOutput = true };
        git.ArgumentList.Add("rev-parse");
        git.ArgumentList.Add("HEAD");
        await using var process = new OwnedProcess(git);
        var sha = (await process.Process.StandardOutput.ReadToEndAsync(ct)).Trim();
        await process.Process.WaitForExitAsync(ct);
        var order = (await File.ReadAllTextAsync(Path.Combine(run.Evidence, "order.txt"), ct)).Trim();
        var successor = (Guid)(await fixture.ScalarAsync(
            $"SELECT term_id FROM payment_fulfilments WHERE predecessor_term_id = '{run.Term:D}'"))!;
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "build-and-postgres.json"), JsonSerializer.Serialize(new
        {
            sha, buildId = ngsw.RootElement.GetProperty("appData").GetProperty("buildId").GetString(),
            renewalOrder = order, predecessorTerm = run.Term, successorTerm = successor,
            predecessor = await P0308Tests.TermRowAsync(fixture, run.Term),
            successor = await P0308Tests.TermRowAsync(fixture, successor),
            provenance = await fixture.ScalarAsync($"""
                SELECT to_char(payment_effective_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') || ' grace=' ||
                    grace_days_included || ' ' || renewal_policy_version FROM payment_fulfilments WHERE order_id = '{order}'
                """),
            successorsOfPredecessor = await fixture.CountAsync("payment_fulfilments", $"predecessor_term_id = '{run.Term:D}'"),
            fulfilments = await fixture.CountAsync("payment_fulfilments"),
            verifiedPayments = await fixture.CountAsync("verified_payments"),
            paidShopLinks = await fixture.CountAsync("payment_fulfilment_shops"),
            pendingSnapshotsAfterFulfilment = pendingSnapshots,
            appliedOutbox = await fixture.CountAsync("entitlement_outbox", "status = 'Applied'"),
            command = "bun x playwright test --config " + Configuration, phases = Phases,
            passed = Phases.Length, skipped = 0, boundary = "Development simulator only; no real payment provider was contacted.",
        }), ct);
    }

    private sealed record Run(string Frontend, string Evidence, string Owner, Guid Account, Guid Term, long AmountPaise);
}

internal sealed class P0308BrowserFactAttribute : FactAttribute
{
    public P0308BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0308_RUN_BROWSER") != "1")
            Skip = "Set P0308_RUN_BROWSER=1 after building the shipping merchant application.";
    }
}
