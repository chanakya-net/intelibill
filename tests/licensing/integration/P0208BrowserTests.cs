using System.Net;
using System.Text.Json;
using Npgsql;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0207BrowserTests;

namespace Intelibill.Licensing.Integration.Tests;

[Collection(MerchantBillingHosts.Name)]
public sealed class P0208BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private static readonly JsonSerializerOptions EvidenceJson = new() { WriteIndented = true };

    [P0208BrowserFact]
    [Trait("Category", "Browser")]
    public async Task RetiredPlanLeavesSelectionWhileAcceptedTermsStayReadable()
    {
        await database.PrepareMerchantAsync();
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        using var signing = new P0201Fixture(database, runtime.Database!);
        await using var licensing = new LicensingApiFactory(runtime, settings: Settings(signing));
        licensing.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var licensingClient = licensing.CreateClient();
        await using var merchant = signing.MerchantApi(licensingClient.BaseAddress!.AbsoluteUri);
        merchant.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var merchantClient = merchant.CreateClient();
        var email = P0202Fixture.UniqueEmail();
        await SeedShopAsync(database, await P0201Tests.SeedMerchantAsync(merchant.Services, email));

        var bootstrap = database.Bootstrap(runtime.Database);
        await P0207Tests.SeedCoverageAsync(bootstrap);
        var coverage = await CoverageAsync(bootstrap);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        await RunDemoAsync(licensing, licensingClient.BaseAddress!, merchantClient.BaseAddress!, email,
            bootstrap, coverage, timeout.Token);
    }

    private async Task RunDemoAsync(LicensingApiFactory licensing, Uri licensingOrigin, Uri merchantOrigin, string email,
        NpgsqlConnectionStringBuilder bootstrap, object? coverage, CancellationToken token)
    {
        var adminRoot = Path.Combine(ProjectPaths.Root, "src/admin-frontend");
        await using var adminWeb = await StartAdminWebAsync(adminRoot, licensingOrigin, token);
        using var staging = new MerchantWebBuild(Path.Combine(ProjectPaths.Root, "src/frontend"));
        await using var merchantWeb = await StartMerchantWebAsync(staging.Root, merchantOrigin, token);
        var evidence = Environment.GetEnvironmentVariable("P0208_EVIDENCE_DIR") is { Length: > 0 } path
            ? Directory.CreateDirectory(path).FullName : Directory.CreateTempSubdirectory("intelibill-p0208-").FullName;
        var setup = Directory.CreateTempSubdirectory("intelibill-p0208-bootstrap-");
        try
        {
            var proof = await WriteBootstrapProofAsync(licensing.Services, setup.FullName, adminWeb.Origin, token);
            await RunBrowserAsync(adminRoot, adminWeb.Origin, merchantWeb.Origin, email, evidence, proof,
                "playwright.p0208.config.ts", output, token);
        }
        finally
        {
            setup.Delete(recursive: true);
        }

        Assert.Equal(coverage, await CoverageAsync(bootstrap));
        await AssertRetainedSnapshotAsync(bootstrap, evidence, token);
        output.WriteLine($"Sanitized retirement browser evidence: {evidence}");
    }

    private static async Task AssertRetainedSnapshotAsync(NpgsqlConnectionStringBuilder bootstrap, string evidence,
        CancellationToken token)
    {
        using var browser = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(evidence, "observations.json"), token));
        var hashBefore = browser.RootElement.GetProperty("snapshotHashBefore").GetString();
        var retained = await ScalarAsync(bootstrap, """
            SELECT concat_ws(':', v.status, v.revision, v.monthly_price_paise, v.annual_price_paise, v.included_shops,
                v.members_per_shop, p.snapshot_hash, upper(encode(sha256(convert_to(p.snapshot, 'UTF8')), 'hex')))
            FROM plan_versions v JOIN catalogue_publications c ON c.version_id = v.id
            JOIN catalogue_preparations p USING (admin_id, operation_key)
            JOIN published_catalogue_offers o ON o.version_id = v.id
            JOIN catalogue_retirements r ON r.version_id = v.id
            JOIN withdrawn_catalogue_offers w ON w.version_id = v.id
            WHERE v.name = 'Fixture Growth' AND v.version_number = 1 AND o.development_fixture
                AND o.snapshot::jsonb = p.snapshot::jsonb -> 'plan'
            """);
        Assert.Equal($"3:3:149900:1499000:3:5:{hashBefore}:{hashBefore}", retained);
        Assert.Equal(1L, await ScalarAsync(bootstrap, "SELECT count(*) FROM catalogue_retirements"));
        Assert.Equal(1L, await ScalarAsync(bootstrap, "SELECT count(*) FROM withdrawn_catalogue_offers"));

        await File.WriteAllTextAsync(Path.Combine(evidence, "postgres-observations.json"),
            JsonSerializer.Serialize(new
            {
                coverageAndAffiliationsUnchanged = true,
                publicationSnapshotHashUnchanged = hashBefore,
                offerSnapshotMatchesPublication = true,
                versionStatus = "retired",
                versionRevision = 3,
                retirementRows = 1,
                publicWithdrawalRows = 1,
            }, EvidenceJson), token);
    }
}

internal sealed class P0208BrowserFactAttribute : FactAttribute
{
    public P0208BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0208_RUN_BROWSER") != "1")
            Skip = "Set P0208_RUN_BROWSER=1 after building both shipping applications.";
    }
}
