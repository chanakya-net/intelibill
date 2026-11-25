using System.Diagnostics;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0605BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    [ReplacementBrowserFact]
    [Trait("Category", "Browser")]
    public async Task RenewalReplacementInChromium()
    {
        var directory = Environment.GetEnvironmentVariable("P0605_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(directory));
        var evidence = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        await using var f = await P0308Fixture.StartAsync(database, P0601Tests.January31, browser: true);
        var (package, term) = await P0605Tests.PrepareAsync(f);
        await P0605Tests.ObservePermitAsync(f, package, P0601Tests.February14, P0601Tests.February14.AddDays(2));
        var original = await P0605Tests.OriginalHistoryDigestAsync(f, package, term);
        var frontend = Path.Combine(ProjectPaths.Root, "src/frontend");
        using var build = new MerchantWebBuild(frontend);
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        await using var web = await StartMerchantWebAsync(build.Root, package.Payer.Client.BaseAddress!, limit.Token);
        await using var browser = new OwnedProcess(Playwright(frontend, evidence, web.Origin, package, term));
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(limit.Token);
        var stderr = browser.Process.StandardError.ReadToEndAsync(limit.Token);
        await PumpAsync(browser.Process, f, package, evidence, limit.Token);
        await browser.Process.WaitForExitAsync(limit.Token);
        var log = await stdout + await stderr;
        await File.WriteAllTextAsync(Path.Combine(evidence, "playwright.log"), log, limit.Token);
        Assert.True(browser.Process.ExitCode == 0 && log.Contains("1 passed", StringComparison.Ordinal), log);
        Assert.Equal(original, await P0605Tests.OriginalHistoryDigestAsync(f, package, term));
        await WriteEvidenceAsync(f, package, term, original, evidence, frontend, limit.Token);
        output.WriteLine($"Chromium and PostgreSQL evidence: {evidence}");
    }

    private static async Task WriteEvidenceAsync(P0308Fixture f, P0308Fixture.Package p, Guid term, string originalHistoryDigest, string evidence,
        string frontend, CancellationToken ct)
    {
        Assert.Equal(2L, await f.CountAsync("verified_payments"));
        Assert.Equal(4L, await f.CountAsync("shop_affiliations"));
        Assert.Equal(1L, await f.CountAsync("coverage_replacements", $"term_id='{term}' AND state='Applied'"));
        Assert.Equal(0L, await f.Checkout.Delivery.MerchantScalarAsync($"SELECT count(*) FROM shop_memberships WHERE shop_id='{p.Shops[3].ShopId}' AND user_id='{p.Payer.UserId}'"));
        var receipt = (string)(await f.ScalarAsync($"""
            SELECT convert_from(i.payload,'UTF8') FROM entitlement_merchant_inbox i
            JOIN entitlement_snapshots s ON s.event_id=i.event_id JOIN entitlement_messages m ON m.event_id=s.event_id
            WHERE s.shop_id='{p.Shops[3].ShopId}' AND i.kind='Acknowledgement'
                AND convert_from(m.payload,'UTF8')::jsonb->'shop'->>'state'='Active'
            ORDER BY s.shop_revision DESC LIMIT 1
            """))!;
        var ack = JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(receipt, EntitlementSnapshotsV1.Json)!;
        Assert.Equal("Applied", ack.Outcome);
        Assert.Equal(ack.ShopRevision, ack.AppliedShopRevision);
        Assert.NotNull(ack.ActivatedReplacementId);
        var git = new ProcessStartInfo("git", "rev-parse HEAD") { WorkingDirectory = ProjectPaths.Root, RedirectStandardOutput = true };
        using var revision = Process.Start(git)!;
        var sha = (await revision.StandardOutput.ReadToEndAsync(ct)).Trim();
        await revision.WaitForExitAsync(ct);
        Assert.Equal(0, revision.ExitCode);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(frontend, "dist/INVENTORY/browser/ngsw.json"), ct));
        var links = (string)(await f.ScalarAsync($"""
            SELECT jsonb_agg(jsonb_build_object('shop',l.shop_id,'term',l.term_id,'coverageFrom',l.coverage_from,
                'replacement',l.replacement_id) ORDER BY l.term_id,l.shop_id)::text FROM payment_fulfilment_shops l WHERE account_id='{p.Account}'
            """))!;
        var payload = new { sha, originalHistoryDigest, links = JsonSerializer.Deserialize<JsonElement>(links), buildId = manifest.RootElement.GetProperty("appData").GetProperty("buildId"), term,
            outgoing = p.Shops[2].ShopId, incoming = p.Shops[3].ShopId, boundary = P0601Tests.February29,
            localSimulator = true, confirmedPayments = 2, retainedAffiliations = 4, ack };
        await File.WriteAllTextAsync(Path.Combine(evidence, "postgres.json"), JsonSerializer.Serialize(payload), ct);
    }

    private static async Task PumpAsync(Process process, P0308Fixture f, P0308Fixture.Package p, string dir, CancellationToken ct)
    {
        async Task<bool> WaitAsync(string marker)
        {
            while (!process.HasExited && !File.Exists(Path.Combine(dir, marker + ".txt"))) await Task.Delay(50, ct);
            return !process.HasExited;
        }
        Task Signal(string marker) => File.WriteAllTextAsync(Path.Combine(dir, marker + ".txt"), marker, ct);
        if (!await WaitAsync("accepted")) return;
        await f.Checkout.Delivery.DrainAsync(false);
        await Signal("scheduled");
        if (!await WaitAsync("paid")) return;
        await f.Checkout.Delivery.DrainAsync(true);
        await f.Checkout.Delivery.DrainAsync(false);
        var boundary = P0601Tests.February29;
        await P0605Tests.VerifyTradingAsync(f, p, boundary.AddTicks(-10), "Allowed", "Licence.PendingActivation");
        Assert.Equal(0L, await f.Checkout.Delivery.MerchantScalarAsync("SELECT count(*) FROM coverage_replacement_activations"));
        await P0605Tests.VerifyTradingAsync(f, p, boundary, "Licence.Expired", "Allowed");
        Assert.Equal(1L, await f.Checkout.Delivery.MerchantScalarAsync("SELECT count(*) FROM coverage_replacement_activations"));
        await Signal("merchant-committed");
        if (!await WaitAsync("applying")) return;
        var sessionClock = f.Hosts.Fixture.Clock;
        var original = sessionClock.Now;
        try
        {
            sessionClock.Now = boundary;
            await f.Checkout.Delivery.DrainAsync(false);
        }
        finally { sessionClock.Now = original; }
        await Signal("delivered");
    }

    private static ProcessStartInfo Playwright(string frontend, string evidence, string origin, P0308Fixture.Package p, Guid term)
    {
        var run = new ProcessStartInfo("bun") { WorkingDirectory = frontend, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", "../../tests/licensing/browser/playwright.p0605.config.ts" })
            run.ArgumentList.Add(arg);
        run.Environment["P0605_EVIDENCE_DIR"] = evidence;
        run.Environment["P0605_WEB"] = origin;
        run.Environment["P0605_PAYER"] = p.Payer.Email;
        run.Environment["P0605_OWNER"] = p.Shops[3].Email;
        run.Environment["P0605_PASSWORD"] = P0201Tests.Password;
        run.Environment["P0605_ACCOUNT"] = p.Account.ToString("D");
        run.Environment["P0605_TERM"] = term.ToString("D");
        run.Environment["P0605_PLAN"] = p.Plan.ToString("D");
        run.Environment["P0605_OUTGOING"] = p.Shops[2].ShopId.ToString("D");
        run.Environment["P0605_SHOP"] = p.Shops[3].ShopId.ToString("D");
        run.Environment["NODE_PATH"] = Path.Combine(frontend, "node_modules");
        return run;
    }
}

internal sealed class ReplacementBrowserFactAttribute : FactAttribute
{
    public ReplacementBrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0605_RUN_BROWSER") != "1")
            Skip = "Set P0605_RUN_BROWSER=1 after building the shipping merchant app.";
    }
}
