using System.Diagnostics;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0301Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0301BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private static readonly string[] Phases = ["review", "expiry"];
    private const string Configuration = "../../../tests/backend/licensing/browser/playwright.p0301.config.ts";

    [P0301BrowserFact]
    [Trait("Category", "Browser")]
    public async Task BillingOwnerReviewsAndAcceptsFrozenFullPaymentQuote()
    {
        var evidence = Environment.GetEnvironmentVariable("P0301_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence), "Set P0301_EVIDENCE_DIR.");
        Directory.CreateDirectory(evidence);
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await StartCheckoutAsync(database, clock, browser: true);
        var run = await PrepareAsync(fixture, evidence);
        using var staged = new MerchantWebBuild(run.Frontend);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await using var web = await StartMerchantWebAsync(staged.Root, run.ApiOrigin, timeout.Token);
        var accessBefore = await AccessEvidenceAsync(fixture);
        await RunAsync(run, web.Origin, "review", timeout.Token);
        await RetirePlanAsync(fixture, run.Unaccepted);
        Assert.Equal(accessBefore, await AccessEvidenceAsync(fixture));
        clock.Now = run.Unaccepted.ExpiresAt;
        await RunAsync(run, web.Origin, "expiry", timeout.Token);
        Assert.Equal(accessBefore, await AccessEvidenceAsync(fixture));
        await RecordDatabaseAsync(fixture, run, timeout.Token);
    }

    private static async Task<BrowserFixture> PrepareAsync(P0210Fixture fixture, string evidence)
    {
        var owner = await SignInAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client, "Checkout Fixture");
        var other = await CreateAccountAsync(owner.Client, "Other Fixture Account");
        var ids = new List<Guid>();
        foreach (var suffix in new[] { "Central", "East", "North", "West" })
        {
            var branch = await SignInAsync(fixture.Hosts, ShopRole.Owner);
            await AffiliateAsync(fixture.LicensingOwner, account, owner.UserId, branch.ShopId, branch.UserId,
                null, P0210Fixture.Start.AddDays(-1), null, "Fixture " + suffix);
            ids.Add(branch.ShopId);
        }
        var version = await PublishPlanAsync(fixture);
        var expires = await CreateQuoteAsync(owner.Client, account, new(Guid.NewGuid(), version, "Month", [.. ids], 1, 1));
        return new(Path.Combine(ProjectPaths.Root, "src/frontend/intelibill"), evidence, owner.Client.BaseAddress!, owner.Email, account, other, expires);
    }

    private async Task RunAsync(BrowserFixture run, string origin, string phase, CancellationToken ct)
    {
        var start = new ProcessStartInfo("bun") { WorkingDirectory = run.Frontend, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", Configuration, "--grep", "@" + phase })
            start.ArgumentList.Add(arg);
        start.Environment["NODE_PATH"] = Path.Combine(run.Frontend, "node_modules");
        start.Environment["P0301_MERCHANT_WEB"] = origin;
        start.Environment["P0301_OWNER"] = run.Owner;
        start.Environment["P0301_PASSWORD"] = P0201Tests.Password;
        start.Environment["P0301_EVIDENCE_DIR"] = run.Evidence;
        start.Environment["P0301_ACCOUNT"] = run.Account.ToString("D");
        start.Environment["P0301_OTHER_ACCOUNT"] = run.OtherAccount.ToString("D");
        start.Environment["P0301_EXPIRED_ORDER"] = run.Unaccepted.OrderId.ToString("D");
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

    private static async Task RecordDatabaseAsync(P0210Fixture db, BrowserFixture run, CancellationToken ct)
    {
        var orders = await db.CountAsync("checkout_orders");
        var accepted = await db.CountAsync("checkout_order_acceptances");
        var shops = await db.CountAsync("checkout_order_shops");
        var terms = await db.CountAsync("subscription_coverage_terms");
        var trials = await db.CountAsync("trial_history");
        Assert.Equal(2L, orders);
        Assert.Equal(1L, accepted);
        Assert.Equal(8L, shops);
        Assert.Equal(0L, terms);
        Assert.Equal(0L, trials);
        using var ngsw = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(run.Frontend, "dist/INVENTORY/browser/ngsw.json"), ct));
        var git = new ProcessStartInfo("git") { WorkingDirectory = ProjectPaths.Root, RedirectStandardOutput = true };
        git.ArgumentList.Add("rev-parse");
        git.ArgumentList.Add("HEAD");
        await using var process = new OwnedProcess(git);
        var sha = (await process.Process.StandardOutput.ReadToEndAsync(ct)).Trim();
        await process.Process.WaitForExitAsync(ct);
        Assert.Equal(0, process.Process.ExitCode);
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "build-and-postgres.json"), JsonSerializer.Serialize(new
        {
            sha, buildId = ngsw.RootElement.GetProperty("appData").GetProperty("buildId").GetString(),
            orders, accepted, shops, terms, trials, unchangedAccessAndProjections = true, catalogueRetiredBeforeExpiryReplay = true, command = "bun x playwright test --config " + Configuration,
            phases = Phases, passed = 2, skipped = 0,
        }), ct);
    }

    private static async Task<object?> AccessEvidenceAsync(P0210Fixture fixture) =>
        await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.LicensingOwner, """
            SELECT jsonb_build_array(
                (SELECT jsonb_agg(to_jsonb(e) ORDER BY account_id) FROM entitlement_account_heads e),
                (SELECT jsonb_agg(to_jsonb(e) ORDER BY shop_id) FROM entitlement_shop_heads e),
                (SELECT count(*) FROM subscription_coverage_terms),
                (SELECT count(*) FROM subscription_term_facts),
                (SELECT count(*) FROM trial_history)
            )::text
            """);

    private sealed record BrowserFixture(string Frontend, string Evidence, Uri ApiOrigin, string Owner, Guid Account,
        Guid OtherAccount, CheckoutQuoteV1 Unaccepted);
}

internal sealed class P0301BrowserFactAttribute : FactAttribute
{
    public P0301BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0301_RUN_BROWSER") != "1")
            Skip = "Set P0301_RUN_BROWSER=1 after building the shipping merchant application.";
    }
}
