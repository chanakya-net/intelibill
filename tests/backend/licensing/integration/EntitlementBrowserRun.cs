using System.Diagnostics;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

internal sealed record EntitlementBrowserRun(string Suite, ITestOutputHelper Output, string Root, string Evidence,
    string Admin, string MerchantOrigin, string OwnerEmail, string StaffEmail, Guid Account, string Proof, string ProviderState)
{
    internal static async Task WithWebAsync(P0213Fixture fixture, Merchant owner, Merchant staff, Guid account,
        string evidence, string suite, ITestOutputHelper output, Func<EntitlementBrowserRun, CancellationToken, Task> exercise)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var adminRoot = Path.Combine(ProjectPaths.Root, "src/admin-frontend");
        await using var adminWeb = await StartAdminWebAsync(adminRoot, fixture.LicensingApi.BaseAddress!, timeout.Token);
        using var staging = new MerchantWebBuild(Path.Combine(ProjectPaths.Root, "src/frontend"));
        await using var merchantWeb = await StartMerchantWebAsync(staging.Root, fixture.MerchantApi.BaseAddress!, timeout.Token);
        var secrets = Directory.CreateTempSubdirectory("intelibill-entitlement-provider-");
        try
        {
            var proof = await P0207BrowserTests.WriteBootstrapProofAsync(fixture.Hosts.Licensing.Services, secrets.FullName,
                adminWeb.Origin, timeout.Token);
            var run = new EntitlementBrowserRun(suite, output, adminRoot, evidence, adminWeb.Origin, merchantWeb.Origin, owner.Email, staff.Email,
                account, proof, Path.Combine(secrets.FullName, "browser-state.json"));
            await exercise(run, timeout.Token);
        }
        finally
        {
            secrets.Delete(recursive: true);
        }
    }

    internal async Task AssertPhaseAsync(P0213Fixture fixture, string phase, CancellationToken ct)
    {
        var before = (fixture.MerchantTraffic.Count, fixture.LicensingTraffic.Count);
        var start = new ProcessStartInfo("bun") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", $"../../tests/backend/licensing/browser/playwright.{Suite}.config.ts",
            "--grep", "@" + phase })
            start.ArgumentList.Add(arg);
        start.Environment["NODE_PATH"] = Path.Combine(Root, "node_modules");
        start.Environment["P0112_ADMIN_WEB"] = Admin;
        start.Environment["P0213_MERCHANT_WEB"] = MerchantOrigin;
        start.Environment["P0213_OWNER_EMAIL"] = OwnerEmail;
        start.Environment["P0213_STAFF_EMAIL"] = StaffEmail;
        start.Environment["P0213_MERCHANT_PASSWORD"] = P0201Tests.Password;
        start.Environment["P0112_PROOF_FILE"] = Proof;
        start.Environment["P0213_PROVIDER_STATE"] = ProviderState;
        start.Environment["P0214_SESSION_DIR"] = Path.GetDirectoryName(ProviderState);
        start.Environment["P0213_ACCOUNT"] = Account.ToString("D");
        start.Environment["P0213_EVIDENCE"] = Evidence;
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await browser.Process.WaitForExitAsync(ct);
        var log = await stdout;
        var errors = await stderr;
        await File.WriteAllTextAsync(Path.Combine(Evidence, $"playwright-{phase}.log"),
            $"bun {string.Join(' ', start.ArgumentList)}\nExit: {browser.Process.ExitCode}\n{log}\n{errors}", ct);
        Output.WriteLine(log);
        Assert.True(browser.Process.ExitCode == 0 && log.Contains("1 passed", StringComparison.Ordinal)
            && File.Exists(Path.Combine(Evidence, $"observations-{phase}.json")), log + errors);
        Assert.Equal(before, (fixture.MerchantTraffic.Count, fixture.LicensingTraffic.Count));
    }
}
