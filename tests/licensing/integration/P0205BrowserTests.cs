using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;

namespace Intelibill.Licensing.Integration.Tests;

[Collection(MerchantBillingHosts.Name)]
public sealed class P0205BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    [P0205BrowserFact]
    [Trait("Category", "Browser")]
    public async Task OwnerConsentAndPayerIsolation()
    {
        await database.PrepareMerchantAsync();
        using var fixture = await P0201Fixture.CreateAsync(database);
        using var key = RSA.Create(2048);
        await using var licensing = P0202Fixture.LicensingApi(fixture, clock: TimeProvider.System,
            settings: P0205Tests.LicensingSettings(fixture, key));
        licensing.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var licensingClient = licensing.CreateClient();
        await using var merchant = fixture.MerchantApi(licensingClient.BaseAddress!.AbsoluteUri, settings: P0205Tests.MerchantSettings(key));
        merchant.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var merchantClient = merchant.CreateClient();
        var payerEmail = P0202Fixture.UniqueEmail();
        var ownerEmail = P0202Fixture.UniqueEmail();
        await P0201Tests.SeedMerchantAsync(merchant.Services, payerEmail);
        var shop = await SeedShopAsync(await P0201Tests.SeedMerchantAsync(merchant.Services, ownerEmail));
        var account = await SeedAccountAsync(merchantClient, payerEmail, fixture);
        var before = await MerchantAuthorityAsync();

        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var staging = new MerchantWebBuild(Path.Combine(ProjectPaths.Root, "src/frontend"));
        await using var web = await StartMerchantWebAsync(staging.Root, merchantClient.BaseAddress!, deadline.Token);
        var evidence = Directory.CreateTempSubdirectory("intelibill-p0205-browser-").FullName;
        output.WriteLine($"Sanitized local Chromium evidence: {evidence}");
        await RunPlaywrightAsync(new BrowserFixture(web.Origin, payerEmail, ownerEmail, shop, account, evidence), deadline.Token);

        Assert.Equal(before, await MerchantAuthorityAsync());
        Assert.Equal(1L, await P0202Fixture.CountAsync(database, fixture, "SELECT count(*) FROM public.shop_affiliations"));
        Assert.Equal(1L, await P0202Fixture.CountAsync(database, fixture, "SELECT count(*) FROM public.shop_affiliation_inbox WHERE status = 'Applied'"));
    }

    private async Task<Guid> SeedShopAsync(Guid owner)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase).ConnectionString)
            .UseSnakeCaseNamingConvention().Options);
        var shop = Shop.Create("Fixture Retail Shop", "Fixture Road", "Bengaluru", "Karnataka", "560001", null, null, null);
        db.Shops.Add(shop);
        db.ShopMemberships.Add(ShopMembership.Create(shop.Id, owner, ShopRole.Owner, true));
        await db.SaveChangesAsync();
        return shop.Id;
    }

    private async Task<Guid> SeedAccountAsync(HttpClient client, string payerEmail, P0201Fixture fixture)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await P0201Tests.LoginAsync(client, payerEmail));
        using var created = await client.PostAsJsonAsync("/api/billing/accounts",
            new { name = "Fixture Payer Account", operationKey = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var account = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accountId").GetGuid();
        var bootstrap = database.Bootstrap(fixture.Owner.Database);
        var now = P0202Fixture.WholeSecondNow();
        await P0205Tests.SeedTermAsync(bootstrap, account, now.AddDays(-7), now.AddDays(23));
        await P0205Tests.SeedTermAsync(bootstrap, account, now.AddDays(-14), now.AddDays(36));
        await P0205Tests.SeedTermAsync(bootstrap, account, now.AddDays(-10), now.AddDays(20), grouped: true);
        return account;
    }

    private async Task<string> MerchantAuthorityAsync() => (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(
        database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase), """
        SELECT string_agg(shop_id || ':' || user_id || ':' || role || ':' || status || ':' || authority_version, ',' ORDER BY id)
        FROM public.shop_memberships
        """))!;

    private static async Task RunPlaywrightAsync(BrowserFixture fixture, CancellationToken ct)
    {
        var root = Path.Combine(ProjectPaths.Root, "src/admin-frontend");
        var start = new ProcessStartInfo("bun")
        {
            WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "x", "playwright", "test", "--config", "../../tests/licensing/browser/playwright.p0205.config.ts" })
            start.ArgumentList.Add(argument);
        start.Environment["NODE_PATH"] = Path.Combine(root, "node_modules");
        start.Environment["P0205_WEB"] = fixture.Web;
        start.Environment["P0205_PAYER"] = fixture.Payer;
        start.Environment["P0205_OWNER"] = fixture.Owner;
        start.Environment["P0205_PASSWORD"] = P0201Tests.Password;
        start.Environment["P0205_SHOP"] = fixture.Shop.ToString("D");
        start.Environment["P0205_ACCOUNT"] = fixture.Account.ToString("D");
        start.Environment["P0205_EVIDENCE"] = fixture.Evidence;

        await using var process = new OwnedProcess(start);
        var stdout = process.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.Process.StandardError.ReadToEndAsync(ct);
        await process.Process.WaitForExitAsync(ct);
        var observations = Path.Combine(fixture.Evidence, "observations.json");
        Assert.True(process.Process.ExitCode == 0 && File.Exists(observations), (await stdout) + (await stderr));
        using var evidence = JsonDocument.Parse(await File.ReadAllBytesAsync(observations, ct));
        Assert.Equal(409, evidence.RootElement.GetProperty("revokedAcceptance").GetInt32());
        Assert.Equal(200, evidence.RootElement.GetProperty("retriedAcceptance").GetInt32());
        Assert.True(evidence.RootElement.GetProperty("conflictingRevokeSentNoRequest").GetBoolean());
    }

    private sealed record BrowserFixture(string Web, string Payer, string Owner, Guid Shop, Guid Account, string Evidence)
    {
        public override string ToString() => "[LOCAL FIXTURE]";
    }
}

internal sealed class P0205BrowserFactAttribute : FactAttribute
{
    public P0205BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0205_RUN_BROWSER") != "1")
            Skip = "Set P0205_RUN_BROWSER=1 after building the merchant web application.";
    }
}
