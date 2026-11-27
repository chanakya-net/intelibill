using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// P06-06 Slice 7: Real shipping Merchant Web and Admin Web builds in Chromium against the real Merchant API,
/// real Licensing API and real PostgreSQL. Proves 3-party owner consents, real Licensing Manager MFA,
/// dropped-response recovery with preserved operation key, Applying vs Acknowledged per-leg states,
/// courtesy bridge expiration and exact paid-slot conservation across the cutover boundary.
/// </summary>
public sealed class P0606BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Configuration = "../../../tests/backend/licensing/browser/playwright.p0606.config.ts";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [P0606BrowserFact]
    [Trait("Category", "Browser")]
    public async Task ReviewEarlyReplacementInChromium()
    {
        var evidence = Environment.GetEnvironmentVariable("P0606_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence), "Set P0606_EVIDENCE_DIR.");
        Directory.CreateDirectory(evidence);
        var scratch = Directory.CreateTempSubdirectory("intelibill-p0606-");
        try { await RunAsync(evidence, scratch.FullName); }
        finally { scratch.Delete(recursive: true); }
    }

    private async Task RunAsync(string evidence, string scratch)
    {
        using var key = RSA.Create(2048);
        await using var f = await P0606Tests.StartPermitFixtureAsync(database, key, January31, browser: true);
        var (p, term) = await P0606Tests.PrepareWithTradingNorthAsync(f);
        var originalHistory = await P0605Tests.OriginalHistoryDigestAsync(f, p, term);
        var permit = await P0606Tests.IssueRealPermitAsync(f, p.Shops[2], P0606Tests.EarlyStart, deliverObservation: true);
        var cutover = permit.ValidUntil;

        const string managerEmail = "browser.manager.p0606@example.test";
        var catalogue = f.Checkout.Trial.Catalogue;
        string managerSeed;
        using (var owner = await catalogue.SignInOwnerAsync())
        {
            managerSeed = await catalogue.EnrollOperatorAsync(owner, managerEmail);
            await P0206Fixture.GrantAsync(owner, managerEmail, "licensing-manager");
        }
        catalogue.Clock.Advance(TimeSpan.FromSeconds(30));

        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var ct = limit.Token;
        var merchantRoot = Path.Combine(ProjectPaths.Root, "src/frontend/intelibill");
        var adminRoot = Path.Combine(ProjectPaths.Root, "src/frontend/licensing");
        using var staged = new MerchantWebBuild(merchantRoot);
        await using var merchantApi = await P0310ApiRelay.StartAsync(p.Payer.Client.BaseAddress!, ct);
        await using var adminApi = await P0310ApiRelay.StartAsync(f.Checkout.Delivery.LicensingApi.BaseAddress!, ct);
        await using var merchant = await StartMerchantWebAsync(staged.Root, merchantApi.Origin, ct);
        await using var admin = await StartAdminWebAsync(adminRoot, adminApi.Origin, ct);
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(f.Checkout, admin.Origin, ct);

        await WriteSecretsAsync(scratch, f, p, managerEmail, managerSeed, ct);
        var start = new ProcessStartInfo("bun")
        {
            WorkingDirectory = adminRoot, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", Configuration })
            start.ArgumentList.Add(arg);
        var env = new Dictionary<string, string>
        {
            ["P0606_MERCHANT_WEB"] = merchant.Origin,
            ["P0606_ADMIN_WEB"] = admin.Origin,
            ["P0606_TEMPORARY"] = scratch,
            ["P0606_EVIDENCE_DIR"] = evidence,
            ["P0606_ACCOUNT"] = p.Account.ToString("D"),
            ["P0606_TERM"] = term.ToString("D"),
            ["P0606_OUTGOING"] = p.Shops[2].ShopId.ToString("D"),
            ["P0606_INCOMING"] = p.Shops[3].ShopId.ToString("D"),
            ["NODE_PATH"] = Path.Combine(adminRoot, "node_modules"),
        };
        foreach (var (k, v) in env) start.Environment[k] = v;

        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await PumpAsync(browser.Process, f, p, cutover, scratch, ct);
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout + await stderr;
        output.WriteLine(report);
        await File.WriteAllTextAsync(Path.Combine(evidence, "playwright.log"), report, ct);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal)
            && !report.Contains("skipped", StringComparison.OrdinalIgnoreCase), report);

        Assert.Equal(originalHistory, await P0605Tests.OriginalHistoryDigestAsync(f, p, term));
        await P0606Tests.AssertSlotConservationOracleAsync(f, p.Account, term, 3, January31, February29);
        Assert.Equal(1L, await f.CountAsync("paid_slot_handoffs"));
        Assert.Equal(1L, await f.CountAsync("complimentary_grants", $"account_id='{p.Account}'"));
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exception_approvals", "state='Applied'"));
        await WriteEvidenceAsync(f, p, term, cutover, originalHistory, evidence, merchantRoot, adminRoot, ct);
    }

    private static async Task PumpAsync(Process process, P0308Fixture f, P0308Fixture.Package p,
        DateTimeOffset cutover, string scratch, CancellationToken ct)
    {
        async Task<bool> WaitAsync(string name)
        {
            while (!process.HasExited && !File.Exists(Path.Combine(scratch, name + ".txt")))
                await Task.Delay(50, ct);
            return !process.HasExited;
        }
        Task Signal(string name) => File.WriteAllTextAsync(Path.Combine(scratch, name + ".txt"), name, ct);

        if (!await WaitAsync("consented")) return;
        await f.Checkout.Delivery.DrainAsync(false);
        await Signal("ready-for-review");

        if (!await WaitAsync("approved")) return;
        // Drain Prepare -> Prepared -> Consume -> Consumed -> Snapshots to Merchant (without delivering Merchant acks yet).
        await P0606Tests.DrainAtAsync(f, P0606Tests.EarlyStart, licensing: true);
        await P0606Tests.DrainAtAsync(f, P0606Tests.EarlyStart, licensing: false);
        await P0606Tests.DrainAtAsync(f, P0606Tests.EarlyStart, licensing: true);
        await P0606Tests.DrainAtAsync(f, P0606Tests.EarlyStart, licensing: false);
        await P0606Tests.DrainAtAsync(f, P0606Tests.EarlyStart, licensing: true);
        await Signal("applying");

        if (!await WaitAsync("saw-applying")) return;
        await P0606Tests.DrainAtAsync(f, P0606Tests.EarlyStart, licensing: false);
        Assert.Equal("Allowed", await P0606Tests.FencedTradingAtAsync(f, p.Shops[2].ShopId, P0606Tests.EarlyStart));
        Assert.Equal("Allowed", await P0606Tests.FencedTradingAtAsync(f, p.Shops[3].ShopId, P0606Tests.EarlyStart));
        await Signal("courtesy-acked");

        if (!await WaitAsync("advance-cutover")) return;
        await P0605Tests.VerifyTradingAsync(f, p, cutover, "Licence.Expired", "Allowed", deliverReceipts: true);
        await Signal("cutover-acked");
    }

    private static async Task WriteSecretsAsync(string scratch, P0308Fixture f, P0308Fixture.Package p,
        string managerEmail, string seed, CancellationToken ct)
    {
        var opts = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) opts.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var file = new FileStream(Path.Combine(scratch, "sessions.json"), opts);
        await JsonSerializer.SerializeAsync(file, new
        {
            admin = managerEmail,
            adminPassword = P0114Tests.InviteePassword,
            seed,
            adminTime = f.Hosts.Fixture.Clock.GetUtcNow().ToUnixTimeSeconds(),
            payer = p.Payer.Email,
            north = p.Shops[2].Email,
            south = p.Shops[3].Email,
            merchantPassword = P0201Tests.Password,
        }, cancellationToken: ct);
    }

    private static async Task WriteEvidenceAsync(P0308Fixture f, P0308Fixture.Package p, Guid term, DateTimeOffset cutover,
        string originalHistoryDigest, string evidence, string merchantRoot, string adminRoot, CancellationToken ct)
    {
        using var ngsw = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(merchantRoot, "dist/INVENTORY/browser/ngsw.json"), ct));
        var adminIndexSha = Convert.ToHexStringLower(SHA256.HashData(
            await File.ReadAllBytesAsync(Path.Combine(adminRoot, "dist/ADMIN/browser/index.html"), ct)));
        var intervals = (string)(await f.ScalarAsync($"""
            SELECT jsonb_agg(jsonb_build_object('origin', slot_origin_shop_id, 'shop', shop_id,
                'start', interval_starts_at, 'end', interval_ends_at)
                ORDER BY slot_origin_shop_id, interval_starts_at)::text
            FROM public.term_paid_slot_intervals('{p.Account}', '{term}')
            """))!;
        await File.WriteAllTextAsync(Path.Combine(evidence, "build-and-postgres.json"), JsonSerializer.Serialize(new
        {
            accountId = p.Account,
            termId = term,
            outgoingShopId = p.Shops[2].ShopId,
            incomingShopId = p.Shops[3].ShopId,
            earlyStart = P0606Tests.EarlyStart,
            paidCutover = cutover,
            normalRenewal = February29,
            originalHistoryDigest,
            paidSlotIntervals = JsonSerializer.Deserialize<JsonElement>(intervals),
            paidSlotHandoffs = await f.CountAsync("paid_slot_handoffs"),
            complimentaryGrants = await f.CountAsync("complimentary_grants", $"account_id='{p.Account}'"),
            merchantBuildId = ngsw.RootElement.GetProperty("appData").GetProperty("buildId").GetString(),
            adminIndexSha,
            passed = 1,
            skipped = 0,
            boundary = "Shipping local merchant/admin builds on real PostgreSQL with local payment simulator only; no live payment provider verified.",
        }, Indented), ct);
    }
}

internal sealed class P0606BrowserFactAttribute : FactAttribute
{
    public P0606BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0606_RUN_BROWSER") != "1")
            Skip = "Set P0606_RUN_BROWSER=1 after building both shipping frontends.";
    }
}
