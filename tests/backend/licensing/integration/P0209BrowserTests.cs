using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Local fixture demonstration: both shipping web applications against real Kestrel hosts and PostgreSQL. The
/// licensing status clock is fixed in this test host only; sessions, delegations and MFA use real time.
/// </summary>
public sealed class P0209BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private static readonly DateTimeOffset EvaluatedAt = Utc("2027-03-02T00:00:00Z");
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [P0209BrowserFact]
    [Trait("Category", "Browser")]
    public async Task MerchantAndProviderExplainTheSameAccessWithoutBlockingCommerce()
    {
        await database.PrepareMerchantAsync();
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase),
            "TRUNCATE public.rate_limit_buckets");
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        using var signing = new P0201Fixture(database, runtime.Database!);
        var licensing = new LicensingApiFactory(runtime, settings: P0207BrowserTests.Settings(signing), configure: services =>
            services.AddKeyedSingleton<TimeProvider>(StatusClockKey, new P0201Clock { Now = EvaluatedAt }));
        var licensingStopped = false;
        try
        {
            licensing.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
            using var licensingClient = licensing.CreateClient();
            await using var merchant = signing.MerchantApi(licensingClient.BaseAddress!.AbsoluteUri);
            merchant.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
            using var merchantClient = merchant.CreateClient();
            var seeded = await SeedAsync(merchant, database.Bootstrap(runtime.Database));
            var evidence = EvidenceDirectory();
            output.WriteLine($"Sanitized local browser evidence: {evidence}");

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
            var adminRoot = Path.Combine(ProjectPaths.Root, "src/frontend/licensing");
            await using var adminWeb = await StartAdminWebAsync(adminRoot, licensingClient.BaseAddress!, timeout.Token);
            using var staging = new MerchantWebBuild(Path.Combine(ProjectPaths.Root, "src/frontend/intelibill"));
            await using var merchantWeb = await StartMerchantWebAsync(staging.Root, merchantClient.BaseAddress!, timeout.Token);
            // The bootstrap proof is a local credential: it lives outside the evidence directory and is deleted after use.
            var secrets = Directory.CreateTempSubdirectory("intelibill-p0209-proof-");
            var proof = await P0207BrowserTests.WriteBootstrapProofAsync(licensing.Services, secrets.FullName, adminWeb.Origin, timeout.Token);
            var browser = new BrowserRun(adminRoot, adminWeb.Origin, merchantWeb.Origin, proof, evidence, seeded);
            var termEvidence = await TermEvidenceAsync(database.Bootstrap(runtime.Database));
            try
            {
                await RunPhaseAsync(browser, "main", timeout.Token);
            }
            finally
            {
                secrets.Delete(recursive: true);
            }

            await PostStockAsync(merchant, seeded.OwnerEmail, "Expired-or-grace stock");
            await SetConnectionsAsync(runtime.Database!, allowed: false);
            try
            {
                await RunPhaseAsync(browser, "storage-outage", timeout.Token);
                await PostStockAsync(merchant, seeded.OwnerEmail, "Licensing storage outage stock");
            }
            finally
            {
                await SetConnectionsAsync(runtime.Database!, allowed: true);
            }
            await RunPhaseAsync(browser, "recovered", timeout.Token);

            await licensing.DisposeAsync();
            licensingStopped = true;
            await RunPhaseAsync(browser, "unreachable", timeout.Token);
            await PostStockAsync(merchant, seeded.OwnerEmail, "Licensing unreachable stock");

            await RecordPostgresEvidenceAsync(evidence, seeded, termEvidence, database.Bootstrap(runtime.Database));
        }
        finally
        {
            if (!licensingStopped) await licensing.DisposeAsync();
        }
    }

    internal sealed record Seeded(Guid Shop, Guid Account, string OwnerEmail, string StaffEmail, string PayerEmail, string Authority)
    {
        public override string ToString() => "[LOCAL FIXTURE]";
    }

    private sealed record BrowserRun(string Root, string Admin, string Merchant, string Proof, string Evidence, Seeded Seeded);

    private async Task<Seeded> SeedAsync(MerchantStartupProbeFactory merchant, NpgsqlConnectionStringBuilder licensing)
    {
        var (ownerEmail, staffEmail, payerEmail) = (P0202Fixture.UniqueEmail(), P0202Fixture.UniqueEmail(), P0202Fixture.UniqueEmail());
        await P0201Tests.SeedMerchantAsync(merchant.Services, ownerEmail);
        var staff = await P0201Tests.SeedMerchantAsync(merchant.Services, staffEmail);
        var payer = await P0201Tests.SeedMerchantAsync(merchant.Services, payerEmail);
        using var owner = await ClientAsync(merchant, ownerEmail);
        var name = "Status Browser " + Guid.NewGuid().ToString("N")[..8];
        using (var created = await owner.PostAsJsonAsync("/api/shops", new
        {
            name, address = "42 MG Road", city = "Bengaluru", state = "Karnataka", pincode = "560001",
            taxProfile = new { stateCode = "29", taxRegime = "Unregistered", eInvoiceStatus = "BelowThreshold", invoiceSeriesCode = "A" },
        }))
            Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        await using var db = MerchantDb(database);
        var shop = await db.Shops.Where(row => row.Name == name).Select(row => row.Id).SingleAsync();
        var ownerId = await db.ShopMemberships.Where(row => row.ShopId == shop).Select(row => row.UserId).SingleAsync();
        db.ShopMemberships.Add(ShopMembership.Create(shop, staff, ShopRole.Staff, true));
        await db.SaveChangesAsync();

        using var payerClient = await ClientAsync(merchant, payerEmail);
        var account = await CreateAccountAsync(payerClient, "Browser Fixture Payer");
        var feb28 = Utc("2027-02-28T04:30:00Z");
        var paid = await SeedTermAsync(licensing, account, new TermSeed(Utc("2027-01-31T04:30:00Z"), feb28,
            AnchorLocal: "2027-01-31 10:00:00"));
        await AffiliateAsync(licensing, account, payer, shop, ownerId, paid, Utc("2027-01-31T04:30:00Z"), feb28);
        await SeedTermAsync(licensing, account, new TermSeed(Utc("2027-02-01T04:30:00Z"), Utc("2027-02-15T04:30:00Z"),
            Basis: "Trial", AnchorLocal: "2027-02-01 10:00:00", PricePaise: null));
        await SeedTermAsync(licensing, account, new TermSeed(Utc("2027-01-01T04:30:00Z"), Utc("2027-04-01T04:30:00Z"),
            Basis: null, Grouped: true, Capacity: 3));
        return new Seeded(shop, account, ownerEmail, staffEmail, payerEmail, await MerchantAuthorityAsync());
    }

    private static async Task<HttpClient> ClientAsync(MerchantStartupProbeFactory merchant, string email)
    {
        var client = merchant.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await P0201Tests.LoginAsync(client, email));
        return client;
    }

    private static async Task PostStockAsync(MerchantStartupProbeFactory merchant, string ownerEmail, string name)
    {
        using var owner = await ClientAsync(merchant, ownerEmail);
        await P0209Tests.PostStockAsync(owner, name);
    }

    private async Task SetConnectionsAsync(string name, bool allowed)
    {
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(), $"""ALTER DATABASE "{name}" ALLOW_CONNECTIONS {allowed}""");
        if (!allowed)
            await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(),
                $"SELECT pg_terminate_backend(pid, 5000) FROM pg_stat_activity WHERE datname = '{name}'");
    }

    private async Task<string> MerchantAuthorityAsync() => (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(
        database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase), """
        SELECT string_agg(shop_id || ':' || user_id || ':' || role || ':' || status || ':' || authority_version, ',' ORDER BY id)
        FROM public.shop_memberships
        """))!;

    private static async Task<string> TermEvidenceAsync(NpgsqlConnectionStringBuilder licensing) =>
        (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(licensing, """
            SELECT json_build_array((SELECT json_agg(t ORDER BY term_id) FROM subscription_coverage_terms t),
                (SELECT json_agg(f ORDER BY term_id) FROM subscription_term_facts f),
                (SELECT json_agg(a ORDER BY request_id) FROM shop_affiliations a),
                (SELECT json_agg(m ORDER BY id) FROM billing_account_members m))::text
            """))!;

    private async Task RecordPostgresEvidenceAsync(string evidence, Seeded seeded, string termEvidence,
        NpgsqlConnectionStringBuilder licensing)
    {
        var merchantDb = database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase);
        var batches = (long)(await LicensingPostgreSqlFixture.ReadScalarAsync(merchantDb,
            $"SELECT count(*) FROM public.inventory_batches WHERE shop_id = '{seeded.Shop:D}'"))!;
        var movements = (long)(await LicensingPostgreSqlFixture.ReadScalarAsync(merchantDb,
            $"SELECT count(*) FROM public.stock_transactions WHERE shop_id = '{seeded.Shop:D}'"))!;
        Assert.Equal(3L, batches);
        Assert.True(movements >= 3, $"stock movements: {movements}");
        Assert.Equal(seeded.Authority, await MerchantAuthorityAsync());
        Assert.Equal(termEvidence, await TermEvidenceAsync(licensing));
        await File.WriteAllTextAsync(Path.Combine(evidence, "postgres-evidence.json"), JsonSerializer.Serialize(new
        {
            environment = "Local Testcontainers PostgreSQL; development fixture terms and prices; no live provider",
            evaluatedAt = EvaluatedAt,
            stockBatchesPostedDuringGraceAndOutages = batches,
            stockMovements = movements,
            merchantMembershipsUnchanged = true,
            licensingTermFactsAndMembershipsUnchanged = true,
            merchantBuild = BuildIdentity(Path.Combine(ProjectPaths.Root, "src/frontend/intelibill/dist/INVENTORY/browser/ngsw.json")),
            adminBuild = File.GetLastWriteTimeUtc(Path.Combine(ProjectPaths.Root, "src/frontend/licensing/dist/ADMIN/browser/index.html")),
        }, Indented));
    }

    private static string? BuildIdentity(string ngsw) => File.Exists(ngsw)
        ? JsonDocument.Parse(File.ReadAllText(ngsw)).RootElement.TryGetProperty("appData", out var data)
          && data.TryGetProperty("buildId", out var id) ? id.ToString() : null
        : null;

    private static string EvidenceDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("P0209_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(configured))
            return Directory.CreateTempSubdirectory("intelibill-p0209-").FullName;
        Directory.CreateDirectory(configured);
        return configured;
    }

    private async Task RunPhaseAsync(BrowserRun run, string phase, CancellationToken token)
    {
        var start = new ProcessStartInfo("bun") { WorkingDirectory = run.Root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", "../../../tests/backend/licensing/browser/playwright.p0209.config.ts", "--grep", "@" + phase })
            start.ArgumentList.Add(arg);
        start.Environment["NODE_PATH"] = Path.Combine(run.Root, "node_modules");
        start.Environment["P0112_ADMIN_WEB"] = run.Admin;
        start.Environment["P0112_PROOF_FILE"] = run.Proof;
        start.Environment["P0209_MERCHANT_WEB"] = run.Merchant;
        start.Environment["P0209_STAFF_EMAIL"] = run.Seeded.StaffEmail;
        start.Environment["P0209_PAYER_EMAIL"] = run.Seeded.PayerEmail;
        start.Environment["P0209_PASSWORD"] = P0201Tests.Password;
        start.Environment["P0209_ACCOUNT"] = run.Seeded.Account.ToString("D");
        start.Environment["P0209_EVIDENCE"] = run.Evidence;
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(token);
        var stderr = browser.Process.StandardError.ReadToEndAsync(token);
        await browser.Process.WaitForExitAsync(token);
        var raw = Path.Combine(run.Evidence, "browser");
        if (Directory.Exists(raw)) Directory.Delete(raw, recursive: true);
        var report = await stdout;
        output.WriteLine(report);
        Assert.True(browser.Process.ExitCode == 0 && File.Exists(Path.Combine(run.Evidence, $"observations-{phase}.json")),
            phase + ": " + report + await stderr);
    }
}

internal sealed class P0209BrowserFactAttribute : FactAttribute
{
    public P0209BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0209_RUN_BROWSER") != "1")
            Skip = "Set P0209_RUN_BROWSER=1 after building both shipping applications.";
    }
}
