using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Intelibill.Licensing.Application.Bootstrap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;

namespace Intelibill.Licensing.Integration.Tests;

[Collection(MerchantBillingHosts.Name)]
public sealed class P0207BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    [P0207BrowserFact]
    [Trait("Category", "Browser")]
    public async Task PublicationAndSuccessorAreVisibleWithoutChangingExistingTerms()
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
        var adminRoot = Path.Combine(ProjectPaths.Root, "src/admin-frontend");
        await using var adminWeb = await StartAdminWebAsync(adminRoot, licensingClient.BaseAddress!, timeout.Token);
        using var staging = new MerchantWebBuild(Path.Combine(ProjectPaths.Root, "src/frontend"));
        await using var merchantWeb = await StartMerchantWebAsync(staging.Root, merchantClient.BaseAddress!, timeout.Token);
        var evidence = Directory.CreateTempSubdirectory("intelibill-p0207-").FullName;
        var proof = await WriteBootstrapProofAsync(licensing.Services, evidence, adminWeb.Origin, timeout.Token);
        try
        {
            await RunBrowserAsync(adminRoot, adminWeb.Origin, merchantWeb.Origin, email, evidence, proof,
                "playwright.p0207.config.ts", output, timeout.Token);
        }
        finally
        {
            File.Delete(proof);
        }

        Assert.Equal(coverage, await CoverageAsync(bootstrap));
        Assert.Equal(2L, await ScalarAsync(bootstrap, """
            SELECT count(*) FROM published_catalogue_offers
            WHERE development_fixture AND snapshot::jsonb ->> 'paymentConfirmationIsTaxInvoice' = 'false'
            """));
        Assert.Equal("1:3:1499000:5,2:2:1799000:7", await ScalarAsync(bootstrap, """
            SELECT string_agg(version_number || ':' || status || ':' || annual_price_paise || ':' || members_per_shop,
                ',' ORDER BY version_number)
            FROM plan_versions WHERE name = 'Fixture Growth'
            """));
        output.WriteLine($"Sanitized catalogue browser evidence: {evidence}");
    }

    internal static Dictionary<string, string?> Settings(P0201Fixture signing)
    {
        var settings = P0108Tests.SignInMfaSettings();
        settings["AdminSessions:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        foreach (var (key, value) in signing.LicensingSettings()) settings[key] = value;
        foreach (var (key, value) in P0207Tests.LocalSettings()) settings[key] = value;
        return settings;
    }

    internal static async Task SeedShopAsync(LicensingPostgreSqlFixture database, Guid owner)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase).ConnectionString)
            .UseSnakeCaseNamingConvention().Options);
        var shop = Shop.Create("Catalogue Fixture Shop", "Fixture Road", "Bengaluru", "Karnataka", "560001", null, null, null);
        db.Shops.Add(shop);
        db.ShopMemberships.Add(ShopMembership.Create(shop.Id, owner, ShopRole.Owner, true));
        await db.SaveChangesAsync();
    }

    internal static Task<object?> ScalarAsync(NpgsqlConnectionStringBuilder settings, string sql) =>
        LicensingPostgreSqlFixture.ReadScalarAsync(settings, sql);

    internal static Task<object?> CoverageAsync(NpgsqlConnectionStringBuilder settings) => ScalarAsync(settings, """
        SELECT json_build_array((SELECT json_agg(t ORDER BY term_id) FROM subscription_coverage_terms t),
            (SELECT json_agg(a ORDER BY request_id) FROM shop_affiliations a))::text
        """);

    internal static async Task<string> WriteBootstrapProofAsync(IServiceProvider services, string evidence, string origin,
        CancellationToken token)
    {
        await using var scope = services.CreateAsyncScope();
        var proof = await scope.ServiceProvider.GetRequiredService<PrepareBootstrapAdminHandler>()
            .PrepareAsync("p0112-owner@example.test", "p0207-local-fixture", origin, true, token);
        Assert.False(proof.IsError);
        var path = Path.Combine(evidence, "proof");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var file = new FileStream(path, options);
        await using var writer = new StreamWriter(file);
        await writer.WriteAsync(proof.Value.Value.AsMemory(), token);
        return path;
    }

    internal static async Task RunBrowserAsync(string root, string admin, string merchant, string email, string evidence,
        string proof, string config, ITestOutputHelper output, CancellationToken token)
    {
        var start = new ProcessStartInfo("bun")
        {
            WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", "../../tests/licensing/browser/" + config })
            start.ArgumentList.Add(arg);
        start.Environment["NODE_PATH"] = Path.Combine(root, "node_modules");
        start.Environment["P0112_ADMIN_WEB"] = admin;
        start.Environment["P0112_PROOF_FILE"] = proof;
        start.Environment["P0207_MERCHANT_WEB"] = merchant;
        start.Environment["P0207_MERCHANT_EMAIL"] = email;
        start.Environment["P0207_MERCHANT_PASSWORD"] = P0201Tests.Password;
        start.Environment["P0207_EVIDENCE"] = evidence;

        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(token);
        var stderr = browser.Process.StandardError.ReadToEndAsync(token);
        await browser.Process.WaitForExitAsync(token);
        var raw = Path.Combine(evidence, "browser");
        if (Directory.Exists(raw)) Directory.Delete(raw, recursive: true);
        var report = await stdout;
        output.WriteLine(report);
        Assert.True(browser.Process.ExitCode == 0 && File.Exists(Path.Combine(evidence, "observations.json")),
            report + await stderr);
    }
}

internal sealed class P0207BrowserFactAttribute : FactAttribute
{
    public P0207BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0207_RUN_BROWSER") != "1")
            Skip = "Set P0207_RUN_BROWSER=1 after building both shipping applications.";
    }
}
