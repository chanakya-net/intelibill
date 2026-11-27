using System.Diagnostics;
using System.Net;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0201BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    [P0201BrowserFact]
    [Trait("Category", "Browser")]
    public async Task MerchantOrigin_PreservesDelegationBoundary()
    {
        await database.PrepareMerchantAsync();
        using var fixture = await P0201Fixture.CreateAsync(database);
        var traffic = new InternalTraffic();
        await using var licensing = fixture.LicensingApi(configure: services =>
        {
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IStartupFilter>(traffic);
        });
        licensing.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var licensingClient = licensing.CreateClient();
        var licensingOrigin = licensingClient.BaseAddress!;
        await using var merchant = fixture.MerchantApi(licensingOrigin.AbsoluteUri);
        merchant.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var merchantClient = merchant.CreateClient();
        var actor = await P0201Tests.SeedMerchantAsync(merchant.Services);
        var allowed = Guid.NewGuid();
        await fixture.GrantAsync(allowed, actor);

        var root = ProjectPaths.Root;
        var merchantRoot = Path.Combine(root, "src/frontend/intelibill");
        var adminRoot = Path.Combine(root, "src/frontend/licensing");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var staging = new MerchantWebBuild(merchantRoot);
        await using var merchantWeb = await StartMerchantWebAsync(staging.Root, merchantClient.BaseAddress!, deadline.Token);
        await using var adminWeb = await StartAdminWebAsync(adminRoot, licensingOrigin, deadline.Token);
        var evidence = Directory.CreateTempSubdirectory("intelibill-p0201-").FullName;
        await RunPlaywrightAsync(adminRoot, merchantWeb.Origin, adminWeb.Origin, licensingOrigin,
            allowed, evidence, deadline.Token);

        Assert.Equal(3, traffic.Requests);
        output.WriteLine("Licensing observed exactly two server delegations and one direct rejected request.");
        output.WriteLine($"Sanitized browser output directory {evidence}");
    }

    private async Task RunPlaywrightAsync(string adminRoot, string merchantWeb, string adminWeb,
        Uri licensingOrigin, Guid account, string evidence, CancellationToken cancellationToken)
    {
        var playwright = new ProcessStartInfo("bun") { WorkingDirectory = adminRoot, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "x", "playwright", "test", "--config", "../../../tests/backend/licensing/browser/playwright.p0201.config.ts" })
            playwright.ArgumentList.Add(argument);
        playwright.Environment["NODE_PATH"] = Path.Combine(adminRoot, "node_modules");
        playwright.Environment["P0201_MERCHANT_WEB"] = merchantWeb;
        playwright.Environment["P0201_ADMIN_WEB"] = adminWeb;
        playwright.Environment["P0201_LICENSING_API"] = licensingOrigin.AbsoluteUri;
        playwright.Environment["P0201_ACCOUNT"] = account.ToString("D");
        playwright.Environment["P0201_DENIED_ACCOUNT"] = Guid.NewGuid().ToString("D");
        playwright.Environment["P0201_EMAIL"] = P0201Tests.Email;
        playwright.Environment["P0201_PASSWORD"] = P0201Tests.Password;
        playwright.Environment["P0201_EVIDENCE"] = evidence;

        await using var run = new OwnedProcess(playwright);
        var stdout = run.Process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = run.Process.StandardError.ReadToEndAsync(cancellationToken);
        await run.Process.WaitForExitAsync(cancellationToken);
        var report = await stdout;
        output.WriteLine(report);
        var errors = await stderr;
        var browserArtifacts = Path.Combine(evidence, "browser");
        if (Directory.Exists(browserArtifacts))
            Directory.Delete(browserArtifacts, recursive: true);

        Assert.True(run.Process.ExitCode == 0 && File.Exists(Path.Combine(evidence, "observations.json")), errors);
    }
}

internal sealed class P0201BrowserFactAttribute : FactAttribute
{
    public P0201BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0201_RUN_BROWSER") != "1")
            Skip = "Set P0201_RUN_BROWSER=1 after building both shipping web applications.";
    }
}
