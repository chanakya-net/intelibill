using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0309BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Config = "../../../tests/backend/licensing/browser/playwright.p0309.config.ts";

    [P0309BrowserFact]
    [Trait("Category", "Browser")]
    public async Task OwnerSeesFreshPaidDatesAndAcknowledgedPropagation()
    {
        var evidence = Environment.GetEnvironmentVariable("P0309_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence), "Set P0309_EVIDENCE_DIR.");
        Directory.CreateDirectory(evidence);
        var frontend = Path.Combine(ProjectPaths.Root, "src/frontend/intelibill");
        await using var fixture = await StartAsync(database, March31, browser: true);
        var package = await fixture.PackageAsync(1, ["Fresh Term Shop"]);
        await P0309Tests.ApplyTrialAsync(fixture, package.Payer);
        await P0309Tests.DrainAsync(fixture);
        fixture.At(March31.AddDays(2));
        var quote = await FirstQuoteAsync(package, "Month");
        using var staged = new MerchantWebBuild(frontend);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        await using var web = await StartMerchantWebAsync(staged.Root, package.Payer.Client.BaseAddress!, timeout.Token);
        var run = new Run(frontend, evidence, web.Origin, package.Payer.Email, package.Account, quote.AmountPaise);

        await RunAsync(run, "trial", timeout.Token);
        var trialOrder = ReadOrder(run, "trial");
        var trial = await MaterializedTermAsync(fixture, trialOrder);
        await P0309Tests.DrainAsync(fixture);
        await ExerciseRestoredAccessAsync(fixture, package, run with { Predecessor = trial }, timeout.Token);
        await RecordAsync(fixture, run, trialOrder, timeout.Token);
    }

    private async Task ExerciseRestoredAccessAsync(P0308Fixture fixture, Package package, Run run, CancellationToken ct)
    {
        fixture.At(Utc("2027-05-10T04:30:00Z"));
        await SetRestrictionAsync(fixture, run.Account, release: false);
        await RunAsync(run, "fresh", ct);
        var order = ReadOrder(run, "fresh");
        Assert.Equal(1L, await fixture.CountAsync("payment_restoration_holds", $"order_id = '{order:D}'"));
        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilments", $"order_id = '{order:D}'"));

        fixture.At(Utc("2027-05-12T04:30:00Z"));
        await SetRestrictionAsync(fixture, run.Account, release: true);
        await RunAsync(run, "retry", ct);
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments", $"order_id = '{order:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("verified_payments", $"order_id = '{order:D}'"));
        await P0309Tests.ApproveAdjustmentAsync(fixture, package, order, Utc("2027-06-12T04:30:00Z"));
        await P0309Tests.DrainAsync(fixture);
        await RunAsync(run, "applied", ct);
    }

    private static Task SetRestrictionAsync(P0308Fixture fixture, Guid account, bool release) =>
        LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            UPDATE public.entitlement_account_heads SET revision = revision + 1,
                restriction = {(release ? "'Unrestricted'" : "'Trading'")}, released_revision = {(release ? "revision" : "NULL")}
            WHERE account_id = '{account:D}'
            """);

    private async Task RunAsync(Run run, string phase, CancellationToken ct)
    {
        var start = new ProcessStartInfo("bun") { WorkingDirectory = run.Frontend, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", Config, "--grep", "@" + phase })
            start.ArgumentList.Add(arg);
        start.Environment["NODE_PATH"] = Path.Combine(run.Frontend, "node_modules");
        start.Environment["P0309_MERCHANT_WEB"] = run.Origin;
        start.Environment["P0309_OWNER"] = run.Owner;
        start.Environment["P0309_PASSWORD"] = P0201Tests.Password;
        start.Environment["P0309_EVIDENCE_DIR"] = run.Evidence;
        start.Environment["P0309_ACCOUNT"] = run.Account.ToString("D");
        start.Environment["P0309_TERM"] = run.Predecessor?.ToString("D") ?? "";
        start.Environment["P0309_TOTAL"] = "₹" + (run.AmountPaise / 100m).ToString("N2", CultureInfo.GetCultureInfo("en-IN"));
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout + await stderr;
        output.WriteLine(report);
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, phase + "-playwright.log"), report, ct);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal)
            && !report.Contains("skipped", StringComparison.OrdinalIgnoreCase), report);
    }

    private static Guid ReadOrder(Run run, string phase) =>
        Guid.Parse(File.ReadAllText(Path.Combine(run.Evidence, phase + "-order.txt")).Trim());

    private static async Task<Guid> MaterializedTermAsync(P0308Fixture fixture, Guid order) =>
        Assert.IsType<Guid>(await fixture.ScalarAsync($"SELECT term_id FROM payment_fulfilments WHERE order_id = '{order:D}'"));

    private static async Task RecordAsync(P0308Fixture fixture, Run run, Guid trialOrder, CancellationToken ct)
    {
        using var ngsw = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(run.Frontend, "dist/INVENTORY/browser/ngsw.json"), ct));
        var git = new ProcessStartInfo("git", "rev-parse HEAD") { WorkingDirectory = ProjectPaths.Root, RedirectStandardOutput = true };
        await using var gitProcess = new OwnedProcess(git);
        var sha = (await gitProcess.Process.StandardOutput.ReadToEndAsync(ct)).Trim();
        await gitProcess.Process.WaitForExitAsync(ct);
        Assert.Equal(0, gitProcess.Process.ExitCode);
        var freshOrder = ReadOrder(run, "fresh");
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "build-and-postgres.json"), JsonSerializer.Serialize(new
        {
            sha, buildId = ngsw.RootElement.GetProperty("appData").GetProperty("buildId").GetString(),
            paid = await fixture.ScalarAsync($"""
                SELECT jsonb_agg(jsonb_build_object('order', f.order_id, 'startsAt', t.starts_at, 'endsAt', t.ends_at,
                    'effectiveAt', e.effective_at, 'readyAt', e.ready_at, 'reviewedRelease', e.released_revision,
                    'kind', e.kind, 'requiredShopRevision', d.shop_revision,
                    'adjustments', (SELECT count(*) FROM service_restoration_adjustments a WHERE a.order_id = f.order_id)))::text
                FROM payment_fulfilments f JOIN paid_fresh_starts e USING (account_id, order_id, term_id)
                JOIN subscription_coverage_terms t USING (account_id, term_id)
                JOIN payment_fulfilment_deliveries d USING (account_id, order_id)
                WHERE f.order_id IN ('{trialOrder:D}', '{freshOrder:D}')
                """),
            verifiedPayments = await fixture.CountAsync("verified_payments"),
            fulfilments = await fixture.CountAsync("payment_fulfilments"),
            appliedOutbox = await fixture.CountAsync("entitlement_outbox", "status = 'Applied'"),
            transport = "Shipping Angular merchant bundle, loopback Kestrel merchant and licensing APIs, separate PostgreSQL databases",
            boundary = "Development simulator only; no live payment provider was contacted.",
        }), ct);
    }

    private sealed record Run(string Frontend, string Evidence, string Origin, string Owner, Guid Account, long AmountPaise,
        Guid? Predecessor = null);
}

internal sealed class P0309BrowserFactAttribute : FactAttribute
{
    public P0309BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0309_RUN_BROWSER") != "1")
            Skip = "Set P0309_RUN_BROWSER=1 after building the merchant application.";
    }
}
