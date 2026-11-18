using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Shipping merchant web build in Chromium against real merchant and licensing APIs (loopback Kestrel), each on
/// its own PostgreSQL database, with the local payment simulator and the signed entitlement transport pumped by
/// hand. Simulator evidence only: no real provider is contacted.
/// </summary>
public sealed class P0302BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Configuration = "../../tests/licensing/browser/playwright.p0302.config.ts";
    private static readonly string[] ShopNames = ["Purchase Central", "Purchase East", "Purchase North"];
    private static readonly string[] Phases = ["pending", "capture", "applied"];

    [P0302BrowserFact]
    [Trait("Category", "Browser")]
    public async Task BillingOwnerCompletesSimulatorPurchaseAndSeesAccessApplied()
    {
        var evidence = Environment.GetEnvironmentVariable("P0302_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence), "Set P0302_EVIDENCE_DIR.");
        Directory.CreateDirectory(evidence);
        await using var fixture = await P0302Fixture.StartAsync(database, new P0201Clock { Now = P0302Fixture.WholeSecondStart }, browser: true);
        var (payer, quote) = await PrepareAsync(fixture);
        var run = new Run(Path.Combine(ProjectPaths.Root, "src/frontend"), evidence, payer.Email, quote);
        using var staged = new MerchantWebBuild(run.Frontend);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        await using var web = await StartMerchantWebAsync(staged.Root, payer.Client.BaseAddress!, timeout.Token);

        await RunAsync(run, web.Origin, "pending", timeout.Token);
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await fixture.CountAsync("subscription_term_facts"));
        var attempt = (Guid)(await fixture.Delivery.SourceScalarAsync("SELECT attempt_id FROM payment_attempts"))!;

        // The simulated provider captures out of band; only the server's own verification can confirm it.
        await P0302Fixture.CaptureAsync(payer.Client, quote, attempt);
        await RunAsync(run, web.Origin, "capture", timeout.Token);
        await P0302Fixture.VerifyAsync(payer.Client, quote, attempt);
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, quote, 3);
        var pending = await fixture.CountAsync("entitlement_outbox", "status IN ('Pending', 'Leased')");
        Assert.True(pending >= 3);

        await fixture.Delivery.DrainAsync(source: true);
        await fixture.Delivery.DrainAsync(source: false);
        await RunAsync(run, web.Origin, "applied", timeout.Token);
        await RecordAsync(fixture, run, pending, timeout.Token);
    }

    private static async Task<(Merchant Payer, CheckoutQuoteV1 Quote)> PrepareAsync(P0302Fixture fixture)
    {
        var payer = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(payer.Client, "Simulator Purchase");
        var shops = new List<Merchant> { payer, await SignInAsync(fixture.Hosts, ShopRole.Owner),
            await SignInAsync(fixture.Hosts, ShopRole.Owner) };
        foreach (var (shop, name) in shops.Zip(ShopNames))
        {
            await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Delivery.MerchantDb,
                $"UPDATE shops SET name = '{name}' WHERE id = '{shop.ShopId:D}'");
            await P0213Fixture.LinkAsync(account, payer, shop);
        }
        await fixture.Delivery.DrainAsync(source: true);
        await fixture.Delivery.DrainAsync(source: false);
        var plan = await P0301Tests.PublishPlanAsync(fixture.Trial);
        var quote = await P0301Tests.CreateQuoteAsync(payer.Client, account,
            new(Guid.NewGuid(), plan, "Month", [.. shops.Select(shop => shop.ShopId)], 0, 1));
        using var accepted = await payer.Client.PostAsJsonAsync(P0301Tests.AcceptPath(quote), P0301Tests.Accept(quote));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        return (payer, quote);
    }

    private async Task RunAsync(Run run, string origin, string phase, CancellationToken ct)
    {
        var start = new ProcessStartInfo("bun") { WorkingDirectory = run.Frontend, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", Configuration, "--grep", "@" + phase })
            start.ArgumentList.Add(arg);
        start.Environment["NODE_PATH"] = Path.Combine(run.Frontend, "node_modules");
        start.Environment["P0302_MERCHANT_WEB"] = origin;
        start.Environment["P0302_OWNER"] = run.Owner;
        start.Environment["P0302_PASSWORD"] = P0201Tests.Password;
        start.Environment["P0302_EVIDENCE_DIR"] = run.Evidence;
        start.Environment["P0302_ACCOUNT"] = run.Quote.AccountId.ToString("D");
        start.Environment["P0302_ORDER"] = run.Quote.OrderId.ToString("D");
        start.Environment["P0302_TOTAL"] = "₹" + (run.Quote.AmountPaise / 100m).ToString("N2", CultureInfo.GetCultureInfo("en-IN"));
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

    private static async Task RecordAsync(P0302Fixture fixture, Run run, long pendingSnapshots, CancellationToken ct)
    {
        using var ngsw = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(run.Frontend, "dist/INVENTORY/browser/ngsw.json"), ct));
        var git = new ProcessStartInfo("git") { WorkingDirectory = ProjectPaths.Root, RedirectStandardOutput = true };
        git.ArgumentList.Add("rev-parse");
        git.ArgumentList.Add("HEAD");
        await using var process = new OwnedProcess(git);
        var sha = (await process.Process.StandardOutput.ReadToEndAsync(ct)).Trim();
        await process.Process.WaitForExitAsync(ct);
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "build-and-postgres.json"), JsonSerializer.Serialize(new
        {
            sha, buildId = ngsw.RootElement.GetProperty("appData").GetProperty("buildId").GetString(),
            amountPaise = run.Quote.AmountPaise, shopCount = run.Quote.ShopCount,
            verifiedPayments = await fixture.CountAsync("verified_payments"),
            fulfilments = await fixture.CountAsync("payment_fulfilments"),
            paidTermFacts = await fixture.CountAsync("subscription_term_facts", "basis = 'Paid'"),
            paidShopLinks = await fixture.CountAsync("payment_fulfilment_shops"),
            pendingSnapshotsAfterFulfilment = pendingSnapshots,
            appliedOutbox = await fixture.CountAsync("entitlement_outbox", "status = 'Applied'"),
            command = "bun x playwright test --config " + Configuration, phases = Phases,
            passed = 3, skipped = 0, boundary = "Development simulator only; no real payment provider was contacted.",
        }), ct);
    }

    private sealed record Run(string Frontend, string Evidence, string Owner, CheckoutQuoteV1 Quote);
}

internal sealed class P0302BrowserFactAttribute : FactAttribute
{
    public P0302BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0302_RUN_BROWSER") != "1")
            Skip = "Set P0302_RUN_BROWSER=1 after building the shipping merchant application.";
    }
}
