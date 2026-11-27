using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0204BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    [P0204BrowserFact]
    [Trait("Category", "Browser")]
    public async Task MerchantWeb_TransfersOwnershipWithoutChangingShopAuthority()
    {
        await database.PrepareMerchantAsync();
        using var fixture = await P0201Fixture.CreateAsync(database);
        await using var licensing = P0202Fixture.LicensingApi(fixture, clock: TimeProvider.System);
        licensing.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var licensingClient = licensing.CreateClient();
        await using var merchant = fixture.MerchantApi(licensingClient.BaseAddress!.AbsoluteUri);
        merchant.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var merchantClient = merchant.CreateClient();

        var ownerEmail = P0202Fixture.UniqueEmail();
        var recipientEmail = P0202Fixture.UniqueEmail();
        var strangerEmail = P0202Fixture.UniqueEmail();
        var owner = await P0201Tests.SeedMerchantAsync(merchant.Services, ownerEmail);
        var recipient = await P0201Tests.SeedMerchantAsync(merchant.Services, recipientEmail);
        var stranger = await P0201Tests.SeedMerchantAsync(merchant.Services, strangerEmail);
        var ownerShop = await SeedShopAsync(owner, ShopRole.Owner);
        await SeedShopAsync(recipient, ShopRole.Staff);
        await SeedShopAsync(stranger, ShopRole.Owner);
        var shopRoles = await ShopAuthoritiesAsync();

        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var staging = new MerchantWebBuild(Path.Combine(ProjectPaths.Root, "src/frontend/intelibill"));
        await using var web = await StartMerchantWebAsync(staging.Root, merchantClient.BaseAddress!, deadline.Token);
        var evidence = Environment.GetEnvironmentVariable("P0204_EVIDENCE")
            ?? Directory.CreateTempSubdirectory("intelibill-p0204-browser-").FullName;
        Directory.CreateDirectory(evidence);
        var launch = new BrowserLaunch(web.Origin, licensingClient.BaseAddress, ownerEmail, recipientEmail, strangerEmail,
            ownerShop, evidence);
        await RunPlaywrightAsync(launch, deadline.Token);

        var rows = await P0202Fixture.ReadAccountRowsAsync(database, fixture);
        Assert.Equal(2, rows.Length);
        Assert.Equal((owner, 2, 1), (rows[0].MerchantUserId, rows[0].Role, rows[0].Status));
        Assert.Equal((recipient, 1, 1), (rows[1].MerchantUserId, rows[1].Role, rows[1].Status));
        Assert.Equal(rows[0].AccountId, rows[1].AccountId);
        Assert.Equal(shopRoles, await ShopAuthoritiesAsync());
        Assert.Equal(1L, await P0202Fixture.CountAsync(database, fixture, "SELECT count(*) FROM billing_ownership_acceptances"));
        output.WriteLine("PostgreSQL confirmed one Owner, former Owner now Manager, one accepted transfer, and unchanged shop authority rows.");

        output.WriteLine($"Sanitized local fixture observations: {evidence}");
    }

    private static async Task RunPlaywrightAsync(BrowserLaunch launch, CancellationToken ct)
    {
        var adminRoot = Path.Combine(ProjectPaths.Root, "src/frontend/licensing");
        var start = new ProcessStartInfo("bun")
        {
            WorkingDirectory = adminRoot, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", "../../../tests/backend/licensing/browser/playwright.p0204.config.ts" })
            start.ArgumentList.Add(arg);
        start.Environment["NODE_PATH"] = Path.Combine(adminRoot, "node_modules");
        start.Environment["P0204_WEB"] = launch.Web;
        start.Environment["P0204_LICENSING"] = launch.Licensing.AbsoluteUri;
        start.Environment["P0204_OWNER"] = launch.Owner;
        start.Environment["P0204_RECIPIENT"] = launch.Recipient;
        start.Environment["P0204_STRANGER"] = launch.Stranger;
        start.Environment["P0204_PASSWORD"] = P0201Tests.Password;
        start.Environment["P0204_OWNER_SHOP"] = launch.OwnerShop.ToString("D");
        start.Environment["P0204_EVIDENCE"] = launch.Evidence;

        await using var run = new OwnedProcess(start);
        var stdout = run.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = run.Process.StandardError.ReadToEndAsync(ct);
        await run.Process.WaitForExitAsync(ct);
        var playwrightOutput = Path.Combine(launch.Evidence, "browser");
        if (Directory.Exists(playwrightOutput))
            Directory.Delete(playwrightOutput, recursive: true);
        var file = Path.Combine(launch.Evidence, "observations.json");
        Assert.True(run.Process.ExitCode == 0 && File.Exists(file), (await stdout) + (await stderr));
        using var evidence = JsonDocument.Parse(await File.ReadAllBytesAsync(file, ct));
        Assert.Equal(202, evidence.RootElement.GetProperty("issue").GetInt32());
        Assert.Equal(200, evidence.RootElement.GetProperty("accept").GetInt32());
        Assert.Equal(200, evidence.RootElement.GetProperty("recovered").GetInt32());
        Assert.Equal(404, evidence.RootElement.GetProperty("stranger").GetInt32());
    }

    private async Task<Guid> SeedShopAsync(Guid actor, ShopRole role)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase).ConnectionString)
            .UseSnakeCaseNamingConvention().Options);
        var shop = Shop.Create("Billing browser shop", "Fixture Road", "Bengaluru", "Karnataka", "560001", null, null, null);
        db.Shops.Add(shop);
        db.ShopMemberships.Add(ShopMembership.Create(shop.Id, actor, role, isDefault: true));
        await db.SaveChangesAsync();
        return shop.Id;
    }

    private async Task<string> ShopAuthoritiesAsync() => (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(
        database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase), """
        SELECT string_agg(shop_id || ':' || user_id || ':' || role || ':' || status || ':' || authority_version, ',' ORDER BY id)
        FROM public.shop_memberships
        """))!;

    private sealed record BrowserLaunch(string Web, Uri Licensing, string Owner, string Recipient, string Stranger,
        Guid OwnerShop, string Evidence)
    {
        public override string ToString() => "[REDACTED]";
    }
}

internal sealed class P0204BrowserFactAttribute : FactAttribute
{
    public P0204BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0204_RUN_BROWSER") != "1")
            Skip = "Set P0204_RUN_BROWSER=1 after building the shipping merchant web application.";
    }
}
