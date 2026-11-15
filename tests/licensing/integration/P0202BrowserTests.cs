using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Local fixture verification: real Chromium against the built merchant web, both APIs on loopback with
/// the production membership adapter, and disposable PostgreSQL. It shows no live provider or key vault.
/// </summary>
[Collection(MerchantBillingHosts.Name)]
public sealed class P0202BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    [P0202BrowserFact]
    [Trait("Category", "Browser")]
    public async Task MerchantWeb_CreatesAndListsBillingAccounts()
    {
        await database.PrepareMerchantAsync();
        using var fixture = await P0201Fixture.CreateAsync(database);
        var traffic = new InternalTraffic();
        await using var licensing = P0202Fixture.LicensingApi(fixture, clock: TimeProvider.System,
            configure: services => services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(traffic));
        licensing.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var licensingClient = licensing.CreateClient();
        var licensingOrigin = licensingClient.BaseAddress!;
        await using var merchant = fixture.MerchantApi(licensingOrigin.AbsoluteUri);
        merchant.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var merchantClient = merchant.CreateClient();

        var emailA = P0202Fixture.UniqueEmail();
        var emailB = P0202Fixture.UniqueEmail();
        var actorA = await P0201Tests.SeedMerchantAsync(merchant.Services, emailA);
        var actorB = await P0201Tests.SeedMerchantAsync(merchant.Services, emailB);
        await SeedShopAsync(actorA);
        var shopB = await SeedShopAsync(actorB);
        var memberships = await CountMerchantAsync("SELECT count(*) FROM shop_memberships");

        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var staging = new MerchantWebBuild(Path.Combine(ProjectPaths.Root, "src/frontend"));
        await using var merchantWeb = await StartMerchantWebAsync(staging.Root, merchantClient.BaseAddress!, deadline.Token);
        var evidence = Directory.CreateTempSubdirectory("intelibill-p0202-").FullName;
        var observations = await RunPlaywrightAsync(merchantWeb.Origin, licensingOrigin, emailA, emailB, shopB,
            evidence, deadline.Token);

        var rows = await P0202Fixture.ReadAccountRowsAsync(database, fixture);
        Assert.Equal(2, rows.Length);
        Assert.All(rows, row => Assert.Equal((actorA, 1, 1), (row.MerchantUserId, row.Role, row.Status)));
        Assert.Equal(
            observations.GetProperty("accountIds").EnumerateArray().Select(id => id.GetGuid()).Order(),
            rows.Select(row => row.AccountId).Order());
        Assert.DoesNotContain(rows, row => row.MerchantUserId == actorB);
        Assert.Equal(memberships, await CountMerchantAsync("SELECT count(*) FROM shop_memberships"));
        Assert.Equal(observations.GetProperty("delegatedCalls").GetInt32(), traffic.Requests);
        output.WriteLine($"Local fixture verification: {traffic.Requests} delegated licensing requests; two accounts owned by A only.");
        output.WriteLine($"Sanitized browser output directory {evidence}");
    }

    private async Task<JsonElement> RunPlaywrightAsync(string merchantWeb, Uri licensingOrigin, string emailA,
        string emailB, Guid shopB, string evidence, CancellationToken cancellationToken)
    {
        var adminRoot = Path.Combine(ProjectPaths.Root, "src/admin-frontend");
        var playwright = new ProcessStartInfo("bun") { WorkingDirectory = adminRoot, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "x", "playwright", "test", "--config", "../../tests/licensing/browser/playwright.p0202.config.ts" })
            playwright.ArgumentList.Add(argument);
        playwright.Environment["NODE_PATH"] = Path.Combine(adminRoot, "node_modules");
        playwright.Environment["P0202_MERCHANT_WEB"] = merchantWeb;
        playwright.Environment["P0202_LICENSING_API"] = licensingOrigin.AbsoluteUri;
        playwright.Environment["P0202_EMAIL_A"] = emailA;
        playwright.Environment["P0202_EMAIL_B"] = emailB;
        playwright.Environment["P0202_PASSWORD"] = P0201Tests.Password;
        playwright.Environment["P0202_SHOP_B"] = shopB.ToString("D");
        playwright.Environment["P0202_MENU_LABEL"] = "Billing accounts";
        playwright.Environment["P0202_EVIDENCE"] = evidence;

        await using var run = new OwnedProcess(playwright);
        var stdout = run.Process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = run.Process.StandardError.ReadToEndAsync(cancellationToken);
        await run.Process.WaitForExitAsync(cancellationToken);
        output.WriteLine(await stdout);
        var errors = await stderr;
        var browserArtifacts = Path.Combine(evidence, "browser");
        if (Directory.Exists(browserArtifacts))
            Directory.Delete(browserArtifacts, recursive: true);

        var file = Path.Combine(evidence, "observations.json");
        Assert.True(run.Process.ExitCode == 0 && File.Exists(file), errors);
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        var text = await File.ReadAllTextAsync(file, cancellationToken);
        Assert.DoesNotContain("Bearer", text, StringComparison.Ordinal);
        Assert.DoesNotContain(P0201Tests.Password, text, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private async Task<Guid> SeedShopAsync(Guid actor)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase).ConnectionString)
            .UseSnakeCaseNamingConvention().Options);
        var shop = Shop.Create("Billing browser shop", "Fixture Road", "Bengaluru", "Karnataka", "560001", null, null, null);
        db.Shops.Add(shop);
        db.ShopMemberships.Add(ShopMembership.Create(shop.Id, actor, ShopRole.Owner, isDefault: true));
        await db.SaveChangesAsync();
        return shop.Id;
    }

    private async Task<long> CountMerchantAsync(string sql) => (long)(await LicensingPostgreSqlFixture.ReadScalarAsync(
        database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase), sql))!;
}

internal sealed class P0202BrowserFactAttribute : FactAttribute
{
    public P0202BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0202_RUN_BROWSER") != "1")
            Skip = "Set P0202_RUN_BROWSER=1 after building the shipping merchant web application.";
    }
}
