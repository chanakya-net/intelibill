using System.Diagnostics;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0604BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    [SlotBrowserFact]
    [Trait("Category", "Browser")]
    public async Task OwnerAssignsUnusedSlotInChromium()
    {
        var directory = Environment.GetEnvironmentVariable("P0604_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(directory));
        var evidence = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        await using var f = await P0308Fixture.StartAsync(database, P0601Tests.January31, browser: true);
        var (package, term) = await P0604Tests.PrepareAsync(f);
        var original = await P0308Tests.TermRowAsync(f, term);
        var frontend = Path.Combine(ProjectPaths.Root, "src/frontend");
        using var build = new MerchantWebBuild(frontend);
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        await using var web = await StartMerchantWebAsync(build.Root, package.Payer.Client.BaseAddress!, limit.Token);
        await using var browser = new OwnedProcess(Playwright(frontend, evidence, web.Origin, package, term));
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(limit.Token);
        var stderr = browser.Process.StandardError.ReadToEndAsync(limit.Token);
        await PumpAsync(browser.Process, f, evidence, limit.Token);
        await browser.Process.WaitForExitAsync(limit.Token);
        var log = await stdout + await stderr;
        await File.WriteAllTextAsync(Path.Combine(evidence, "playwright.log"), log, limit.Token);
        Assert.True(browser.Process.ExitCode == 0 && log.Contains("1 passed", StringComparison.Ordinal), log);
        Assert.Equal(original, await P0308Tests.TermRowAsync(f, term));
        var receipt = await VerifyPersistenceAsync(f, package, term);
        var git = new ProcessStartInfo("git", "rev-parse HEAD") { WorkingDirectory = ProjectPaths.Root, RedirectStandardOutput = true };
        await using var revision = new OwnedProcess(git);
        var sha = (await revision.Process.StandardOutput.ReadToEndAsync(limit.Token)).Trim();
        await revision.Process.WaitForExitAsync(limit.Token);
        Assert.Equal(0, revision.Process.ExitCode);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(frontend, "dist/INVENTORY/browser/ngsw.json"), limit.Token));
        var buildId = manifest.RootElement.GetProperty("appData").GetProperty("buildId").GetString();
        var payload = new
        {
            sha, buildId, term, paidThrough = P0601Tests.February29, assignedAt = P0601Tests.February14,
            shop = package.Shops[2].ShopId, name = "North",
            merchantReceipt = new
            {
                receipt.EventId, receipt.Outcome, receipt.Disposition, receipt.AccountRevision, receipt.ShopRevision,
                receipt.AppliedAccountRevision, receipt.AppliedShopRevision, receipt.AppliedAt, receipt.Digest,
            },
            paidLinks = 3, verifiedPayments = 1, payerMembershipsInNorth = 0,
        };
        await File.WriteAllTextAsync(Path.Combine(evidence, "postgres.json"), JsonSerializer.Serialize(payload), limit.Token);
        output.WriteLine($"Chromium and PostgreSQL evidence: {evidence}");
    }

    private static async Task PumpAsync(Process process, P0308Fixture f, string dir, CancellationToken ct)
    {
        async Task<bool> WaitAsync(string marker)
        {
            while (!process.HasExited && !File.Exists(Path.Combine(dir, marker))) await Task.Delay(50, ct);
            return !process.HasExited;
        }
        if (!await WaitAsync("accepted.txt")) return;
        await f.Checkout.Delivery.DrainAsync(false);
        await File.WriteAllTextAsync(Path.Combine(dir, "licensed.txt"), "applied", ct);
        if (!await WaitAsync("applying.txt")) return;
        // Deliver ONLY licensing -> merchant so the browser verifies the intermediate state before the return receipt.
        await f.Checkout.Delivery.DrainAsync(true);
        await File.WriteAllTextAsync(Path.Combine(dir, "merchant-committed.txt"), "committed", ct);
        if (!await WaitAsync("committed-observed.txt")) return;
        // Now deliver merchant -> licensing return receipt so licensing consumes the acknowledgement.
        await f.Checkout.Delivery.DrainAsync(false);
        await File.WriteAllTextAsync(Path.Combine(dir, "delivered.txt"), "delivered", ct);
    }

    private static async Task<EntitlementAcknowledgementV1> VerifyPersistenceAsync(P0308Fixture f, P0308Fixture.Package p, Guid term)
    {
        var shop = p.Shops[2].ShopId;
        Assert.Equal(3L, await f.CountAsync("payment_fulfilment_shops", $"term_id='{term}'"));
        Assert.Equal(1L, await f.CountAsync("coverage_slot_assignments", $"term_id='{term}' AND shop_id='{shop}' AND state='Applied' AND applied_at='2028-02-14T04:30:00Z'"));
        Assert.Equal(1L, await f.CountAsync("checkout_orders"));
        Assert.Equal(1L, await f.CountAsync("verified_payments"));
        Assert.Equal(1L, await f.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM shop_license_states WHERE shop_id='{shop}' AND term_id='{term}' AND starts_at='2028-02-14T04:30:00Z' AND next_boundary='2028-02-29T04:30:00Z'"));
        Assert.Equal(0L, await f.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM shop_memberships WHERE shop_id='{shop}' AND user_id='{p.Payer.UserId}'"));

        var deliveryEventId = (Guid)(await f.ScalarAsync(
            $"SELECT event_id FROM public.coverage_slot_assignment_deliveries WHERE shop_id='{shop}'"))!;
        var receiptBytes = (byte[])(await f.ScalarAsync($"""
            SELECT payload FROM public.entitlement_merchant_inbox
            WHERE event_id='{deliveryEventId}' AND kind='Acknowledgement'
            """))!;
        var receipt = JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(receiptBytes, EntitlementSnapshotsV1.Json)!;
        Assert.Equal(deliveryEventId, receipt.EventId);
        Assert.Equal("Applied", receipt.Outcome);
        Assert.Equal("Applied", receipt.Disposition);
        Assert.Equal(shop, receipt.ShopId);
        Assert.Equal(receipt.ShopRevision, receipt.AppliedShopRevision);
        return receipt;
    }

    private static ProcessStartInfo Playwright(string frontend, string evidence, string origin, P0308Fixture.Package p, Guid term)
    {
        var run = new ProcessStartInfo("bun") { WorkingDirectory = frontend, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", "../../tests/licensing/browser/playwright.p0604.config.ts" })
            run.ArgumentList.Add(arg);
        run.Environment["P0604_EVIDENCE_DIR"] = evidence;
        run.Environment["P0604_WEB"] = origin;
        run.Environment["P0604_PAYER"] = p.Payer.Email;
        run.Environment["P0604_OWNER"] = p.Shops[2].Email;
        run.Environment["P0604_PASSWORD"] = P0201Tests.Password;
        run.Environment["P0604_ACCOUNT"] = p.Account.ToString("D");
        run.Environment["P0604_TERM"] = term.ToString("D");
        run.Environment["P0604_SHOP"] = p.Shops[2].ShopId.ToString("D");
        run.Environment["NODE_PATH"] = Path.Combine(frontend, "node_modules");
        return run;
    }
}

internal sealed class SlotBrowserFactAttribute : FactAttribute
{
    public SlotBrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0604_RUN_BROWSER") != "1")
            Skip = "Set P0604_RUN_BROWSER=1 after building the shipping merchant app.";
    }
}
