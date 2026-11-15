using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;

namespace Intelibill.Licensing.Integration.Tests;

[Collection(MerchantBillingHosts.Name)]
public sealed partial class P0203bBrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    [P0203bBrowserFact]
    [Trait("Category", "Browser")]
    public async Task MerchantWeb_RemovesManagerAndClearsRevokedAccountViews()
    {
        await database.PrepareMerchantAsync();
        var fixture = await P0201Fixture.CreateAsync(database);
        var licensing = P0202Fixture.LicensingApi(fixture, clock: TimeProvider.System);
        licensing.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var licensingClient = licensing.CreateClient();
        var merchant = fixture.MerchantApi(licensingClient.BaseAddress!.AbsoluteUri);
        merchant.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var merchantClient = merchant.CreateClient();
        await using var scenario = new P0203bTests.Scenario(database, fixture, licensing, merchant);
        await scenario.ArrangeAsync();
        var otherAccount = await P0203bTests.CreateAsync(scenario.Manager);

        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var staging = new MerchantWebBuild(Path.Combine(ProjectPaths.Root, "src/frontend"));
        await using var web = await StartMerchantWebAsync(staging.Root, merchantClient.BaseAddress!, deadline.Token);
        var evidence = Directory.CreateTempSubdirectory("intelibill-p0203b-browser-").FullName;
        await RunPlaywrightAsync(new Dictionary<string, string?>
        {
            ["WEB"] = web.Origin, ["LICENSING"] = licensingClient.BaseAddress.AbsoluteUri,
            ["OWNER"] = scenario.OwnerEmail, ["MANAGER"] = scenario.ManagerEmail,
            ["PASSWORD"] = P0201Tests.Password, ["ACCOUNT"] = scenario.Account.ToString("D"),
            ["MEMBER"] = scenario.Member.ToString("D"), ["SHOP"] = scenario.Shop.ToString("D"),
            ["OTHER"] = otherAccount.ToString("D"), ["EVIDENCE"] = evidence,
        }, deadline.Token);

        Assert.Equal(1L, await scenario.CountAsync("SELECT count(*) FROM billing_member_removals WHERE after_revision = 2"));
        Assert.Equal(1L, await scenario.CountAsync(
            $"SELECT count(*) FROM billing_account_members WHERE account_id = '{scenario.Account:D}' AND role = 1 AND status = 1"));
        Assert.Equal(scenario.ShopAuthorities, await P0203bTests.ShopAuthoritiesAsync(database));
        output.WriteLine("Sanitized local fixture observations: " + evidence);
        output.WriteLine(await File.ReadAllTextAsync(Path.Combine(evidence, "observations.json"), deadline.Token));
    }

    private static async Task RunPlaywrightAsync(Dictionary<string, string?> settings, CancellationToken ct)
    {
        var adminRoot = Path.Combine(ProjectPaths.Root, "src/admin-frontend");
        var start = new ProcessStartInfo("bun")
        {
            WorkingDirectory = adminRoot, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", "../../tests/licensing/browser/playwright.p0203b.config.ts" })
            start.ArgumentList.Add(arg);
        start.Environment["NODE_PATH"] = Path.Combine(adminRoot, "node_modules");
        foreach (var (name, value) in settings)
            start.Environment["P0203B_" + name] = value;

        await using var run = new OwnedProcess(start);
        var stdout = run.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = run.Process.StandardError.ReadToEndAsync(ct);
        await run.Process.WaitForExitAsync(ct);
        var evidence = settings["EVIDENCE"]!;
        var playwrightOutput = Path.Combine(evidence, "browser");
        if (Directory.Exists(playwrightOutput))
            Directory.Delete(playwrightOutput, recursive: true);
        var file = Path.Combine(evidence, "observations.json");
        var diagnostics = CredentialLine().Replace((await stdout) + (await stderr), "[fixture credential redacted]");
        Assert.True(run.Process.ExitCode == 0 && File.Exists(file), diagnostics);
        using var observations = JsonDocument.Parse(await File.ReadAllBytesAsync(file, ct));
        Assert.Equal(0, observations.RootElement.GetProperty("cancelRequests").GetInt32());
        Assert.Equal(200, observations.RootElement.GetProperty("replay").GetInt32());
        Assert.Equal(403, observations.RootElement.GetProperty("affiliationDenied").GetInt32());
        Assert.Equal(403, observations.RootElement.GetProperty("rosterDenied").GetInt32());
    }

    [GeneratedRegex("(?im)^.*authorization:.*$")]
    private static partial Regex CredentialLine();

    private sealed class P0203bBrowserFactAttribute : FactAttribute
    {
        public P0203bBrowserFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("P0203B_RUN_BROWSER") != "1")
                Skip = "Set P0203B_RUN_BROWSER=1 after building the shipping merchant web application.";
        }
    }
}
