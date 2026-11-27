using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0211BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private static readonly DateTimeOffset OriginalEnd = P0210Fixture.Start.AddDays(14);
    private static readonly DateTimeOffset ExtendedEnd = Utc("2027-03-10T13:15:30.123456Z");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string Config = "../../../tests/backend/licensing/browser/playwright.p0211.config.ts";

    [P0211BrowserFact]
    [Trait("Category", "Browser")]
    public async Task ProviderApprovesAnExactTrialEndAndTheMerchantReadsIt()
    {
        var evidence = Environment.GetEnvironmentVariable("P0211_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence));
        Directory.CreateDirectory(evidence);
        await using var hosts = await StartHostsAsync();
        var owner = await CreateTradingShopAsync(hosts);
        var staff = await SignInAsync(hosts, ShopRole.Staff, owner.ShopId);
        var account = await CreateAccountAsync(owner.Client);
        await AffiliateAsync(hosts, account, owner.UserId, owner.ShopId, owner.UserId, null, P0210Fixture.Start.AddDays(-1), null);
        var authority = await P0211Tests.MembershipsAsync(database);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var ct = timeout.Token;
        var adminRoot = Path.Combine(ProjectPaths.Root, "src/frontend/licensing");
        using var licensingClient = hosts.Licensing.CreateClient();
        await using var adminWeb = await StartAdminWebAsync(adminRoot, licensingClient.BaseAddress!, ct);
        using var staging = new MerchantWebBuild(Path.Combine(ProjectPaths.Root, "src/frontend/intelibill"));
        await using var merchantWeb = await StartMerchantWebAsync(staging.Root, owner.Client.BaseAddress!, ct);
        var temporary = Directory.CreateTempSubdirectory("intelibill-p0211-");
        var run = new BrowserRun(adminRoot, adminWeb.Origin, merchantWeb.Origin, evidence, temporary.FullName, account, owner, staff);
        try
        {
            await P0207BrowserTests.WriteBootstrapProofAsync(hosts.Licensing.Services, run.Temporary, run.Admin, ct);
            await RunAsync(run, "start", ct);
            var before = await TrialFactsAsync(hosts.LicensingOwner, owner.ShopId);
            await RunAsync(run, "extension", ct);
            hosts.StatusClock.Now = OriginalEnd;
            await RunAsync(run, "active", ct);
            hosts.StatusClock.Now = ExtendedEnd;
            await RunAsync(run, "expired", ct);
            await P0209Tests.PostStockAsync(owner.Client, "Stock at extended trial boundary");
            Assert.Equal(authority, await P0211Tests.MembershipsAsync(database));
            await RecordAsync(run, hosts, before, ct);
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    private async Task<Hosts> StartHostsAsync()
    {
        await database.PrepareMerchantAsync();
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase),
            "TRUNCATE public.rate_limit_buckets");
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var signing = new P0201Fixture(database, runtime.Database!);
        using var delivery = RSA.Create(2048);
        var lifecycle = new P0201Clock { Now = P0210Fixture.Start };
        var settings = P0207BrowserTests.Settings(signing);
        settings[$"{ShopTrialsV1.DeliverySection}:PublicKeys:trial-fixture"] = delivery.ExportSubjectPublicKeyInfoPem();
        var licensing = new LicensingApiFactory(runtime, settings: settings, configure: services =>
        {
            services.AddKeyedSingleton<TimeProvider>(StatusClockKey, lifecycle);
            services.AddKeyedSingleton<TimeProvider>(ShopTrialsV1.DeliverySection, lifecycle);
        });
        licensing.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var client = licensing.CreateClient();
        var merchant = signing.MerchantApi(client.BaseAddress!.AbsoluteUri,
            configure: services => services.AddKeyedSingleton<TimeProvider>(ShopTrialsV1.DeliverySection, lifecycle),
            settings: new Dictionary<string, string?>
            {
                [$"{ShopTrialsV1.DeliverySection}:LocalPrivateKeyPem"] = delivery.ExportPkcs8PrivateKeyPem(),
                [$"{ShopTrialsV1.DeliverySection}:LocalKeyId"] = "trial-fixture",
            });
        merchant.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        return new Hosts(signing, licensing, merchant, lifecycle, database);
    }

    private async Task RunAsync(BrowserRun run, string phase, CancellationToken ct)
    {
        var start = new ProcessStartInfo("bun") { WorkingDirectory = run.Root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "x", "playwright", "test", "--config", Config, "--grep", "@" + phase })
            start.ArgumentList.Add(argument);
        start.Environment["NODE_PATH"] = Path.Combine(run.Root, "node_modules");
        start.Environment["P0112_ADMIN_WEB"] = run.Admin;
        start.Environment["P0112_PROOF_FILE"] = Path.Combine(run.Temporary, "proof");
        start.Environment["P0207_MERCHANT_WEB"] = run.Merchant;
        start.Environment["P0211_TEMPORARY"] = run.Temporary;
        start.Environment["P0211_EVIDENCE_DIR"] = run.Evidence;
        start.Environment["P0211_ACCOUNT"] = run.Account.ToString("D");
        start.Environment["P0211_SHOP"] = run.Owner.ShopId.ToString("D");
        start.Environment["P0207_MERCHANT_PASSWORD"] = P0201Tests.Password;
        start.Environment["P0207_MERCHANT_EMAIL"] = phase is "start" or "extension" ? run.Owner.Email : run.Staff.Email;
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await browser.Process.WaitForExitAsync(ct);
        var log = await stdout;
        output.WriteLine(log);
        Assert.True(browser.Process.ExitCode == 0 && log.Contains("1 passed", StringComparison.Ordinal)
            && !log.Contains("skipped", StringComparison.Ordinal)
            && File.Exists(Path.Combine(run.Evidence, $"observations-{phase}.json")), log + await stderr);
    }

    private async Task RecordAsync(BrowserRun run, Hosts hosts, string before, CancellationToken ct)
    {
        Assert.Equal(before, await TrialFactsAsync(hosts.LicensingOwner, run.Owner.ShopId));
        using var facts = JsonDocument.Parse((string)(await LicensingPostgreSqlFixture.ReadScalarAsync(hosts.LicensingOwner, """
            SELECT json_build_object('extensions', (SELECT count(*) FROM trial_extensions),
                'receipts', (SELECT count(*) FROM trial_extension_receipts),
                'commercialTermFacts', (SELECT count(*) FROM subscription_term_facts),
                'deadline', (SELECT ends_at FROM trial_extensions), 'reason', (SELECT reason FROM trial_extension_receipts),
                'reviewReference', (SELECT review_reference FROM trial_extension_receipts))::text
            """))!);
        var persisted = facts.RootElement;
        Assert.Equal(1, persisted.GetProperty("extensions").GetInt32());
        Assert.Equal(1, persisted.GetProperty("receipts").GetInt32());
        Assert.Equal(0, persisted.GetProperty("commercialTermFacts").GetInt32());
        Assert.Equal(ExtendedEnd, persisted.GetProperty("deadline").GetDateTimeOffset());
        var stock = await LicensingPostgreSqlFixture.ReadScalarAsync(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase),
            $"SELECT count(*) FROM inventory_batches WHERE shop_id = '{run.Owner.ShopId:D}'");
        Assert.Equal(1L, stock);
        await WriteEvidenceAsync(run, "postgres-evidence.json", new
        {
            environment = "Local Testcontainers PostgreSQL", persisted, originalFactsUnchanged = true,
            merchantMembershipsUnchanged = true, observeOnlyStockBatchesAtExpiry = stock,
        }, ct);
        await RecordBuildAsync(run, ct);
        var observations = new Dictionary<string, JsonElement>();
        foreach (var phase in new[] { "start", "extension", "active", "expired" })
        {
            using var read = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(run.Evidence, $"observations-{phase}.json"), ct));
            observations.Add(phase, read.RootElement.Clone());
        }
        await WriteEvidenceAsync(run, "observations.json", observations, ct);
    }

    private static async Task RecordBuildAsync(BrowserRun run, CancellationToken ct)
    {
        var command = new ProcessStartInfo("git") { WorkingDirectory = ProjectPaths.Root, RedirectStandardOutput = true };
        command.ArgumentList.Add("rev-parse");
        command.ArgumentList.Add("HEAD");
        await using var git = new OwnedProcess(command);
        var sha = (await git.Process.StandardOutput.ReadToEndAsync(ct)).Trim();
        await git.Process.WaitForExitAsync(ct);
        Assert.Equal(0, git.Process.ExitCode);
        using var worker = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(ProjectPaths.Root, "src/frontend/intelibill/dist/INVENTORY/browser/ngsw.json"), ct));
        var adminIndex = Path.Combine(run.Root, "dist/ADMIN/browser/index.html");
        await WriteEvidenceAsync(run, "build-evidence.json", new
        {
            sha, merchantBuildId = worker.RootElement.GetProperty("appData").GetProperty("buildId").GetString(),
            adminIndexSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(adminIndex, ct))),
            lifecycleClock = new { start = P0210Fixture.Start, originalEnd = OriginalEnd, extendedEnd = ExtendedEnd },
            provenance = "Local Chromium, shipping web builds, real Kestrel applications and PostgreSQL fixtures.",
        }, ct);
    }

    private static async Task<string> TrialFactsAsync(NpgsqlConnectionStringBuilder owner, Guid shop) =>
        (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(owner, $"""
            SELECT jsonb_build_array(to_jsonb(h), to_jsonb(t), to_jsonb(i))::text
            FROM trial_history h JOIN subscription_coverage_terms t ON t.term_id = h.term_id
            JOIN shop_trial_inbox i ON i.event_id = h.event_id WHERE h.shop_id = '{shop:D}'
            """))!;

    private static Task WriteEvidenceAsync(BrowserRun run, string file, object value, CancellationToken ct) =>
        File.WriteAllTextAsync(Path.Combine(run.Evidence, file), JsonSerializer.Serialize(value, Json), ct);

    private sealed record BrowserRun(string Root, string Admin, string Merchant, string Evidence, string Temporary,
        Guid Account, Merchant Owner, Merchant Staff);
}

internal sealed class P0211BrowserFactAttribute : FactAttribute
{
    public P0211BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0211_RUN_BROWSER") != "1")
            Skip = "Set P0211_RUN_BROWSER=1 after building both shipping applications.";
    }
}
