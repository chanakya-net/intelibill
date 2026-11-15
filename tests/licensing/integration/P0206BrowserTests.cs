using System.Diagnostics;
using System.Net;
using Intelibill.Licensing.Application.Bootstrap;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Real Chromium against the shipping admin web and a real licensing API on a real migrated PostgreSQL database.
/// Nothing is intercepted; amounts are local INR fixture values only.
/// </summary>
public sealed class P0206BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string OwnerEmail = "p0112-owner@example.test";

    [P0206BrowserFact]
    [Trait("Category", "Browser")]
    public async Task LicensingManagerDraftsCatalogueThroughAdminConsole()
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var settings = P0108Tests.SignInMfaSettings();
        settings["AdminSessions:SigningKey"] = Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await using var licensing = new LicensingApiFactory(runtime, clock: TimeProvider.System, settings: settings);
        licensing.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var client = licensing.CreateClient();
        var adminRoot = Path.Combine(ProjectPaths.Root, "src/admin-frontend");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        await using var adminWeb = await StartAdminWebAsync(adminRoot, client.BaseAddress!, deadline.Token);

        var evidence = Directory.CreateTempSubdirectory("intelibill-p0206-").FullName;
        var proofFile = Path.Combine(evidence, "proof");
        await using (var scope = licensing.Services.CreateAsyncScope())
        {
            var proof = await scope.ServiceProvider.GetRequiredService<PrepareBootstrapAdminHandler>()
                .PrepareAsync(OwnerEmail, "p0206-local-fixture", adminWeb.Origin, true, CancellationToken.None);
            Assert.False(proof.IsError);
            await File.WriteAllTextAsync(proofFile, proof.Value.Value, deadline.Token);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(proofFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        try
        {
            await RunPlaywrightAsync(adminRoot, adminWeb.Origin, proofFile, evidence, deadline.Token);
        }
        finally
        {
            File.Delete(proofFile);
        }
        await AssertPersistedAsync(runtime);
        output.WriteLine($"Sanitized browser evidence (observations.json + screenshots): {evidence}");
    }

    private async Task AssertPersistedAsync(Npgsql.NpgsqlConnectionStringBuilder runtime)
    {
        var superuser = database.Bootstrap(runtime.Database);
        async Task<object?> Scalar(string sql) => await LicensingPostgreSqlFixture.ReadScalarAsync(superuser, sql);
        Assert.Equal(2L, await Scalar("SELECT count(*) FROM plan_versions WHERE status = 1"));
        Assert.Equal(0L, await Scalar("SELECT count(*) FROM plan_versions WHERE status <> 1"));
        Assert.Equal(1L, await Scalar("SELECT count(*) FROM trial_policy_versions WHERE status = 1 AND duration_days = 14"));
        Assert.Equal(1L, await Scalar("""
            SELECT count(*) FROM plan_versions v JOIN plans p ON p.id = v.plan_id
            WHERE p.kind = 3 AND v.negotiation_reason = 'Twelve-shop chain negotiated annual terms'
            """));
        Assert.Equal(1L, await Scalar("""
            SELECT count(*) FROM plan_versions WHERE name = 'Growth (local fixture)' AND monthly_price_paise = 149900
              AND annual_price_paise = 1499000 AND included_shops = 3 AND members_per_shop = 5
            """));
        var revisions = (long)(await Scalar("SELECT sum(revision) FROM plan_versions"))!
            + (long)(await Scalar("SELECT sum(revision) FROM trial_policy_versions"))!;
        Assert.Equal(revisions, await Scalar("SELECT count(*) FROM catalogue_draft_changes"));
    }

    private async Task RunPlaywrightAsync(string adminRoot, string adminWeb, string proofFile, string evidence,
        CancellationToken cancellationToken)
    {
        var playwright = new ProcessStartInfo("bun")
        {
            WorkingDirectory = adminRoot, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "x", "playwright", "test", "--config", "../../tests/licensing/browser/playwright.p0206.config.ts" })
            playwright.ArgumentList.Add(argument);
        playwright.Environment["NODE_PATH"] = Path.Combine(adminRoot, "node_modules");
        playwright.Environment["P0112_ADMIN_WEB"] = adminWeb;
        playwright.Environment["P0112_PROOF_FILE"] = proofFile;
        playwright.Environment["P0206_EVIDENCE"] = evidence;

        await using var run = new OwnedProcess(playwright);
        var stdout = run.Process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = run.Process.StandardError.ReadToEndAsync(cancellationToken);
        await run.Process.WaitForExitAsync(cancellationToken);
        output.WriteLine(await stdout);
        var errors = await stderr;
        var raw = Path.Combine(evidence, "browser");
        if (Directory.Exists(raw)) Directory.Delete(raw, recursive: true);
        Assert.True(run.Process.ExitCode == 0 && File.Exists(Path.Combine(evidence, "observations.json")), errors);
    }
}

internal sealed class P0206BrowserFactAttribute : FactAttribute
{
    public P0206BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0206_RUN_BROWSER") != "1")
            Skip = "Set P0206_RUN_BROWSER=1 after building the shipping admin web application.";
    }
}
