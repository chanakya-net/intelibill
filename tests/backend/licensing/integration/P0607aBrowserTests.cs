using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// P06-07a: the shipping merchant web build in Chromium against the real merchant API, real licensing API and real
/// PostgreSQL. Real signed-in owners request a transfer of the named shop and give the three consents, recovering the
/// same decisions after lost responses. After the request, after partial consent and after all three consents the
/// test proves from PostgreSQL that no affiliation, money or access fact moved. Local simulator only.
/// </summary>
public sealed class P0607aBrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Configuration = "../../tests/backend/licensing/browser/playwright.p0607a.config.ts";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [P0607aBrowserFact]
    [Trait("Category", "Browser")]
    public async Task RequestAndConsentInChromium()
    {
        var evidence = Environment.GetEnvironmentVariable("P0607A_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence), "Set P0607A_EVIDENCE_DIR.");
        Directory.CreateDirectory(evidence);
        var scratch = Directory.CreateTempSubdirectory("intelibill-p0607a-");
        try { await RunAsync(evidence, scratch.FullName); }
        finally { scratch.Delete(recursive: true); }
    }

    private async Task RunAsync(string evidence, string scratch)
    {
        using var key = RSA.Create(2048);
        await using var s = await P0607aTests.StartAsync(database, key, browser: true);
        var before = await s.BaselineAsync();

        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var ct = limit.Token;
        var root = Path.Combine(ProjectPaths.Root, "src/frontend");
        using var staged = new MerchantWebBuild(root);
        await using var api = await P0310ApiRelay.StartAsync(s.Source.Payer.Client.BaseAddress!, ct);
        await using var web = await StartMerchantWebAsync(staged.Root, api.Origin, ct);
        await WriteSecretsAsync(scratch, s, ct);

        var start = new ProcessStartInfo("bun")
        {
            WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", Configuration })
            start.ArgumentList.Add(arg);
        foreach (var (name, value) in new Dictionary<string, string>
        {
            ["P0607A_MERCHANT_WEB"] = web.Origin, ["P0607A_TEMPORARY"] = scratch, ["P0607A_EVIDENCE_DIR"] = evidence,
            ["P0607A_SOURCE_ACCOUNT"] = s.Source.Account.ToString("D"),
            ["P0607A_DESTINATION_ACCOUNT"] = s.Destination.Account.ToString("D"),
            ["P0607A_TERM"] = s.Term.ToString("D"), ["P0607A_SHOP"] = s.North.ShopId.ToString("D"),
            ["NODE_PATH"] = Path.Combine(root, "node_modules"),
        })
            start.Environment[name] = value;

        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        var checkpoints = await PumpAsync(browser.Process, s, before, scratch, ct);
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout + await stderr;
        output.WriteLine(report);
        await File.WriteAllTextAsync(Path.Combine(evidence, "playwright.log"), report, ct);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal)
            && !report.Contains("skipped", StringComparison.OrdinalIgnoreCase), report);

        Assert.Equal(["requested", "partial", "collected"], checkpoints.Select(c => c.Name));
        Assert.Equal(before, await s.BaselineAsync());
        await WriteEvidenceAsync(s, before, checkpoints, evidence, root, ct);
    }

    // At each browser checkpoint: every affiliation, money and coverage fact is byte-identical and North still trades.
    private static async Task<List<Checkpoint>> PumpAsync(Process process, P0607aTests.Scenario s, string before,
        string scratch, CancellationToken ct)
    {
        var seen = new List<Checkpoint>();
        async Task<bool> WaitAsync(string name)
        {
            while (!process.HasExited && !File.Exists(Path.Combine(scratch, name + ".txt")))
                await Task.Delay(50, ct);
            return !process.HasExited;
        }
        Task Signal(string name) => File.WriteAllTextAsync(Path.Combine(scratch, name + ".txt"), name, ct);
        async Task CheckAsync(string name, long proposals, long consents)
        {
            Assert.Equal(before, await s.BaselineAsync());
            Assert.Equal(proposals, await s.F.CountAsync("coverage_transfer_proposals"));
            Assert.Equal(consents, await s.F.CountAsync("coverage_transfer_consents", "kind='Accepted'"));
            var trading = await P0606Tests.FencedTradingAtAsync(s.F, s.North.ShopId, P0607aTests.RequestedAt);
            Assert.Equal("Allowed", trading);
            seen.Add(new(name, proposals, consents, trading!, true));
            await Signal(name + "-checked");
        }

        if (!await WaitAsync("requested")) return seen;
        await CheckAsync("requested", 1, 0);
        if (!await WaitAsync("partial")) return seen;
        await CheckAsync("partial", 1, 1);
        if (!await WaitAsync("shop-consented")) return seen;
        Assert.Equal(2L, await s.F.CountAsync("coverage_transfer_consents"));
        await s.F.Checkout.Delivery.DrainAsync(false);
        await Signal("delivered");
        if (!await WaitAsync("collected")) return seen;
        await CheckAsync("collected", 1, 3);
        return seen;
    }

    private static async Task WriteSecretsAsync(string scratch, P0607aTests.Scenario s, CancellationToken ct)
    {
        var opts = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) opts.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var file = new FileStream(Path.Combine(scratch, "sessions.json"), opts);
        await JsonSerializer.SerializeAsync(file, new
        {
            payer = s.Source.Payer.Email,
            destination = s.Destination.Payer.Email,
            north = s.North.Email,
            outsider = s.Source.Shops[1].Email,
            merchantPassword = P0201Tests.Password,
        }, cancellationToken: ct);
    }

    private static async Task WriteEvidenceAsync(P0607aTests.Scenario s, string baseline, List<Checkpoint> checkpoints,
        string evidence, string root, CancellationToken ct)
    {
        using var ngsw = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "dist/INVENTORY/browser/ngsw.json"), ct));
        var consents = (string)(await s.F.ScalarAsync("""
            SELECT jsonb_agg(jsonb_build_object('purpose', purpose, 'kind', kind, 'generation', generation) ORDER BY recorded_at, purpose)::text
            FROM public.coverage_transfer_consents
            """))!;
        await File.WriteAllTextAsync(Path.Combine(evidence, "build-and-postgres.json"), JsonSerializer.Serialize(new
        {
            sourceAccountId = s.Source.Account,
            destinationAccountId = s.Destination.Account,
            shopId = s.North.ShopId,
            shopName = "North",
            checkpoints,
            unchangedBaselineSha256 = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(baseline))),
            consents = JsonSerializer.Deserialize<JsonElement>(consents),
            proposals = await s.F.CountAsync("coverage_transfer_proposals"),
            paidSlotHandoffs = await s.F.CountAsync("paid_slot_handoffs"),
            complimentaryGrants = await s.F.CountAsync("complimentary_grants"),
            merchantBuildId = ngsw.RootElement.GetProperty("appData").GetProperty("buildId").GetString(),
            passed = 1,
            skipped = 0,
            boundary = "Shipping local merchant build on real PostgreSQL with the local payment simulator only; no live provider verified.",
        }, Indented), ct);
    }

    private sealed record Checkpoint(string Name, long Proposals, long AcceptedConsents, string NorthTrading, bool BaselineUnchanged);
}

internal sealed class P0607aBrowserFactAttribute : FactAttribute
{
    public P0607aBrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0607A_RUN_BROWSER") != "1")
            Skip = "Set P0607A_RUN_BROWSER=1 after building the shipping merchant frontend.";
    }
}
