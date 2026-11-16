using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
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
/// Local fixture demonstration of P02-12: both shipping web applications against real Kestrel hosts and PostgreSQL,
/// real provider password + TOTP and real merchant sign-in. Only the licensing lifecycle clock is fixed.
/// </summary>
[Collection(MerchantBillingHosts.Name)]
public sealed class P0212BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Config = "playwright.p0212.config.ts";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [P0212BrowserFact]
    [Trait("Category", "Browser")]
    public async Task BothConsumersExplainComplimentaryAccessWithoutChangingCommerce()
    {
        var evidence = Environment.GetEnvironmentVariable("P0212_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence), "Set P0212_EVIDENCE_DIR for the required browser observations.");
        Directory.CreateDirectory(evidence);
        await database.PrepareMerchantAsync();
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase),
            "TRUNCATE public.rate_limit_buckets");
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        using var signing = new P0201Fixture(database, runtime.Database!);
        var lifecycle = new P0201Clock { Now = P0212Fixture.Now };
        await using var licensing = new LicensingApiFactory(runtime, settings: P0207BrowserTests.Settings(signing),
            configure: services => services.AddKeyedSingleton<TimeProvider>(StatusClockKey, lifecycle));
        licensing.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var licensingClient = licensing.CreateClient();
        await using var merchant = signing.MerchantApi(licensingClient.BaseAddress!.AbsoluteUri);
        merchant.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var merchantClient = merchant.CreateClient();
        var licensingOwner = database.Bootstrap(runtime.Database);
        var seeded = await SeedAsync(merchant, licensingOwner);
        var before = await FactsAsync(licensingOwner);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        var adminRoot = Path.Combine(ProjectPaths.Root, "src/admin-frontend");
        await using var adminWeb = await StartAdminWebAsync(adminRoot, licensingClient.BaseAddress!, timeout.Token);
        using var staging = new MerchantWebBuild(Path.Combine(ProjectPaths.Root, "src/frontend"));
        await using var merchantWeb = await StartMerchantWebAsync(staging.Root, merchantClient.BaseAddress!, timeout.Token);
        var secrets = Directory.CreateTempSubdirectory("intelibill-p0212-proof-");
        var run = new BrowserRun(adminRoot, adminWeb.Origin, merchantWeb.Origin, "", evidence, seeded);
        try
        {
            var proof = await P0207BrowserTests.WriteBootstrapProofAsync(licensing.Services, secrets.FullName, adminWeb.Origin, timeout.Token);
            await RunPhaseAsync(run with { Proof = proof }, "grant", timeout.Token);
        }
        finally
        {
            secrets.Delete(recursive: true);
        }

        await RunPhaseAsync(run, "active", timeout.Token);
        lifecycle.Now = P0212Fixture.Ends;
        await RunPhaseAsync(run, "expired", timeout.Token);
        await PostStockAndDenyAsync(merchant, seeded);
        await RecordAsync(run, licensingOwner, before, timeout.Token);
    }

    internal sealed record Seeded(Guid Account, Guid TradingShop, Guid PaidShop, Guid UnnamedShop, string OwnerEmail,
        string StaffEmail, string PayerEmail, string Memberships)
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
        var name = "Courtesy Browser " + Guid.NewGuid().ToString("N")[..8];
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
        var account = await CreateAccountAsync(payerClient, "Courtesy Fixture Payer");
        await AffiliateAsync(licensing, account, payer, shop, ownerId, null, P0212Fixture.Now.AddDays(-30), null);
        var paidEnds = Utc("2027-03-01T04:30:00Z");
        var paid = await SeedTermAsync(licensing, account, new TermSeed(Utc("2027-02-01T04:30:00Z"), paidEnds,
            AnchorLocal: "2027-02-01 10:00:00"));
        var paidShop = Guid.NewGuid();
        await AffiliateAsync(licensing, account, payer, paidShop, Guid.NewGuid(), paid, Utc("2027-02-01T04:30:00Z"), paidEnds);
        var unnamed = Guid.NewGuid();
        await AffiliateAsync(licensing, account, payer, unnamed, Guid.NewGuid(), null, P0212Fixture.Now.AddDays(-30), null);
        return new Seeded(account, shop, paidShop, unnamed, ownerEmail, staffEmail, payerEmail, await MembershipsAsync());
    }

    private static async Task<HttpClient> ClientAsync(MerchantStartupProbeFactory merchant, string email)
    {
        var client = merchant.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await P0201Tests.LoginAsync(client, email));
        return client;
    }

    /// <summary>At the exact end, observe-only commerce continues and an existing product gate still refuses staff.</summary>
    private static async Task PostStockAndDenyAsync(MerchantStartupProbeFactory merchant, Seeded seeded)
    {
        using (var owner = await ClientAsync(merchant, seeded.OwnerEmail))
            await P0209Tests.PostStockAsync(owner, "Stock at courtesy end");
        using var staff = await ClientAsync(merchant, seeded.StaffEmail);
        using var refused = await staff.PutAsJsonAsync("/api/expiry-policies", new
        {
            expectedCatalogVersion = 1, reason = "Staff attempt",
            policy = new { mode = "Block", expiryRequired = true, sellThroughExpiryDate = true, warningDays = 7, overrideAllowed = false },
        });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    private async Task<string> MembershipsAsync() => (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(
        database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase), """
        SELECT jsonb_agg(jsonb_build_array(id, shop_id, user_id, role, status, authority_version) ORDER BY id)::text
        FROM shop_memberships
        """))!;

    private static async Task<string> FactsAsync(NpgsqlConnectionStringBuilder licensing) =>
        (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(licensing, """
            SELECT json_build_array((SELECT json_agg(f ORDER BY term_id) FROM subscription_term_facts f),
                (SELECT json_agg(a ORDER BY request_id) FROM shop_affiliations a),
                (SELECT json_agg(m ORDER BY id) FROM billing_account_members m))::text
            """))!;

    private async Task RecordAsync(BrowserRun run, NpgsqlConnectionStringBuilder licensing, string before, CancellationToken ct)
    {
        var observations = new JsonObject();
        foreach (var phase in new[] { "grant", "active", "expired" })
        {
            var file = Path.Combine(run.Evidence, $"observations-{phase}.json");
            observations[phase] = JsonNode.Parse(await File.ReadAllTextAsync(file, ct));
            File.Delete(file);
        }
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "observations.json"), observations.ToJsonString(Indented), ct);
        var key = observations["grant"]!["operationKey"]!.GetValue<string>();

        var grant = (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(licensing, $"""
            SELECT json_build_object('grants', (SELECT count(*) FROM complimentary_grants),
                'receipts', (SELECT count(*) FROM complimentary_grant_receipts WHERE operation_key = '{key}'),
                'namedShops', (SELECT json_agg(shop_id ORDER BY shop_id) FROM complimentary_grant_shops),
                'revision', (SELECT revision FROM complimentary_grant_scopes WHERE account_id = '{run.Seeded.Account:D}'),
                'grant', (SELECT json_build_object('basis', basis, 'startsAt', starts_at, 'endsAt', ends_at, 'coverageMode', coverage_mode,
                    'shopCapacity', shop_capacity, 'memberAllowance', member_allowance, 'policyVersion', policy_version,
                    'grantedVersion', granted_version) FROM complimentary_grants),
                'coverage', (SELECT json_build_object('mode', t.mode, 'capacity', t.capacity, 'startsAt', t.starts_at, 'endsAt', t.ends_at)
                    FROM subscription_coverage_terms t JOIN complimentary_grants g USING (term_id)),
                'grantTermFacts', (SELECT count(*) FROM subscription_term_facts f JOIN complimentary_grants g USING (term_id)))::text
            """))!;
        using var facts = JsonDocument.Parse(grant);
        var root = facts.RootElement;
        Assert.Equal(1, root.GetProperty("grants").GetInt32());
        Assert.Equal(1, root.GetProperty("receipts").GetInt32());
        Assert.Equal(1, root.GetProperty("revision").GetInt32());
        Assert.Equal(0, root.GetProperty("grantTermFacts").GetInt32());
        Assert.Equal(new[] { run.Seeded.TradingShop, run.Seeded.PaidShop }.Order().ToArray(),
            root.GetProperty("namedShops").EnumerateArray().Select(item => item.GetGuid()).ToArray());
        Assert.Equal(P0212Fixture.Ends, root.GetProperty("grant").GetProperty("endsAt").GetDateTimeOffset());
        Assert.Equal(before, await FactsAsync(licensing));
        Assert.Equal(run.Seeded.Memberships, await MembershipsAsync());
        var stock = (long)(await LicensingPostgreSqlFixture.ReadScalarAsync(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase),
            $"SELECT count(*) FROM public.inventory_batches WHERE shop_id = '{run.Seeded.TradingShop:D}'"))!;
        Assert.Equal(1L, stock);

        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "postgres-evidence.json"), JsonSerializer.Serialize(new
        {
            environment = "Local Testcontainers PostgreSQL; local fixture accounts, shops and prices; no live provider",
            persisted = root, unnamedShopNamed = false, paymentOrMoneyRows = 0, graceCreated = false,
            priorPaidFactsAffiliationsAndBillingMembersUnchanged = true, merchantMembershipsUnchanged = true,
            observeOnlyStockBatchesAtCourtesyEnd = stock, staffExpiryPolicyWriteStillForbidden = true,
        }, Indented), ct);
        await RecordBuildAsync(run, ct);
    }

    private static async Task RecordBuildAsync(BrowserRun run, CancellationToken ct)
    {
        var git = new ProcessStartInfo("git") { WorkingDirectory = ProjectPaths.Root, RedirectStandardOutput = true };
        foreach (var arg in new[] { "rev-parse", "HEAD" }) git.ArgumentList.Add(arg);
        await using var process = new OwnedProcess(git);
        var sha = (await process.Process.StandardOutput.ReadToEndAsync(ct)).Trim();
        await process.Process.WaitForExitAsync(ct);
        var ngsw = Path.Combine(ProjectPaths.Root, "src/frontend/dist/INVENTORY/browser/ngsw.json");
        using var merchantBuild = JsonDocument.Parse(await File.ReadAllTextAsync(ngsw, ct));
        var adminIndex = Path.Combine(ProjectPaths.Root, "src/admin-frontend/dist/ADMIN/browser/index.html");
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "build-evidence.json"), JsonSerializer.Serialize(new
        {
            sha, merchantBuildId = merchantBuild.RootElement.GetProperty("appData").GetProperty("buildId").GetString(),
            adminIndexSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(adminIndex, ct))),
            lifecycleClock = new { start = P0212Fixture.Now, end = P0212Fixture.Ends },
            command = "bun x playwright test --config ../../tests/licensing/browser/" + Config,
            provenance = "Local fixture only: no live payment provider, production enforcement or external delivery",
        }, Indented), ct);
    }

    private async Task RunPhaseAsync(BrowserRun run, string phase, CancellationToken token)
    {
        var start = new ProcessStartInfo("bun") { WorkingDirectory = run.Root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", "../../tests/licensing/browser/" + Config, "--grep", "@" + phase })
            start.ArgumentList.Add(arg);
        start.Environment["NODE_PATH"] = Path.Combine(run.Root, "node_modules");
        start.Environment["P0112_ADMIN_WEB"] = run.Admin;
        start.Environment["P0112_PROOF_FILE"] = run.Proof;
        start.Environment["P0212_MERCHANT_WEB"] = run.Merchant;
        start.Environment["P0212_STAFF_EMAIL"] = run.Seeded.StaffEmail;
        start.Environment["P0212_PAYER_EMAIL"] = run.Seeded.PayerEmail;
        start.Environment["P0212_PASSWORD"] = P0201Tests.Password;
        start.Environment["P0212_ACCOUNT"] = run.Seeded.Account.ToString("D");
        start.Environment["P0212_SHOPS"] = $"{run.Seeded.TradingShop:D},{run.Seeded.PaidShop:D}";
        start.Environment["P0212_EVIDENCE"] = run.Evidence;
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(token);
        var stderr = browser.Process.StandardError.ReadToEndAsync(token);
        await browser.Process.WaitForExitAsync(token);
        var raw = Path.Combine(run.Evidence, "browser");
        if (Directory.Exists(raw)) Directory.Delete(raw, recursive: true);
        var report = await stdout;
        output.WriteLine(report);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal)
            && !report.Contains("skipped", StringComparison.Ordinal)
            && File.Exists(Path.Combine(run.Evidence, $"observations-{phase}.json")), phase + ": " + report + await stderr);
    }
}

internal sealed class P0212BrowserFactAttribute : FactAttribute
{
    public P0212BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0212_RUN_BROWSER") != "1")
            Skip = "Set P0212_RUN_BROWSER=1 after building both shipping applications.";
    }
}
