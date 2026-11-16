using System.Diagnostics;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0213Tests;

namespace Intelibill.Licensing.Integration.Tests;

[Collection(MerchantBillingHosts.Name)]
public sealed class P0213BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [P0213BrowserFact]
    [Trait("Category", "Browser")]
    public async Task ExistingBillingViewsRemainObserveOnlyDuringDeliveryRecovery()
    {
        var evidence = Environment.GetEnvironmentVariable("P0213_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence), "Set P0213_EVIDENCE_DIR for browser observations.");
        Directory.CreateDirectory(evidence);
        await using var fixture = await P0213Fixture.StartAsync(database, browser: true);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var staff = await SignInAsync(fixture.Hosts, ShopRole.Staff, owner.ShopId);
        var account = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(account, owner, owner);
        await DeliverRestrictionAsync(fixture, account);
        var before = await EvidenceCountAsync(fixture, owner.ShopId);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var adminRoot = Path.Combine(ProjectPaths.Root, "src/admin-frontend");
        await using var adminWeb = await StartAdminWebAsync(adminRoot, fixture.LicensingApi.BaseAddress!, timeout.Token);
        using var staging = new MerchantWebBuild(Path.Combine(ProjectPaths.Root, "src/frontend"));
        await using var merchantWeb = await StartMerchantWebAsync(staging.Root, fixture.MerchantApi.BaseAddress!, timeout.Token);
        var secrets = Directory.CreateTempSubdirectory("intelibill-p0213-provider-");
        try
        {
            var proof = await P0207BrowserTests.WriteBootstrapProofAsync(fixture.Hosts.Licensing.Services, secrets.FullName,
                adminWeb.Origin, timeout.Token);
            var run = new BrowserRun(adminRoot, evidence, adminWeb.Origin, merchantWeb.Origin, owner.Email, staff.Email,
                account, proof, Path.Combine(secrets.FullName, "browser-state.json"));
            await PrepareAndDemonstrateAsync(fixture, run, owner, before, timeout.Token);
        }
        finally
        {
            secrets.Delete(recursive: true);
        }
    }

    private async Task PrepareAndDemonstrateAsync(P0213Fixture fixture, BrowserRun run, Merchant owner, long before, CancellationToken ct)
    {
        await AssertBrowserPhaseAsync(fixture, run, "prepare", ct);
        var policy = (Guid)(await fixture.SourceScalarAsync("SELECT id FROM public.trial_policy_versions WHERE status = 2"))!;
        var revision = (int)(await fixture.SourceScalarAsync($"SELECT revision FROM public.trial_policy_versions WHERE id = '{policy:D}'"))!;
        fixture.Transport.LeaseClock.Now = P0202Fixture.WholeSecondNow().AddMinutes(1);
        var acceptance = new ShopTrialAcceptanceV1(Guid.NewGuid(), policy, revision, "trial-local-1", true);
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(owner, acceptance)).Status);
        var trial = await fixture.LatestSnapshotAsync(owner.ShopId);
        Assert.Equal("Pending", await OutboxAsync(fixture, trial.EventId));
        await AssertBrowserPhaseAsync(fixture, run, "delayed", ct);
        Assert.Equal(before, await EvidenceCountAsync(fixture, owner.ShopId));

        var times = await RecoverAsync(fixture, owner.ShopId, trial, before);
        await AssertBrowserPhaseAsync(fixture, run, "recovered", ct);
        await AssertOutageAsync(fixture, run, ct);
        fixture.Hosts.StatusClock.Now = P0210Fixture.Start.AddDays(14);
        fixture.Transport.LeaseClock.Now = P0202Fixture.WholeSecondNow().AddMinutes(1);
        await fixture.ScanAsync(source: true);
        await fixture.DrainAsync(source: true);
        await AssertBrowserPhaseAsync(fixture, run, "expired", ct);
        await WriteEvidenceAsync(fixture, run, trial, owner.ShopId, before, times, ct);
    }

    private static async Task DeliverRestrictionAsync(P0213Fixture fixture, Guid account)
    {
        // Restricted policy facts stand in for the future authority. Transport, projection and receipts are real.
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            UPDATE public.entitlement_account_heads SET revision = revision + 1, restriction = 'Trading'
            WHERE account_id = '{account:D}'
            """);
        await fixture.ScanAsync(source: false);
        await fixture.DrainAsync(source: false);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        Assert.Equal("Trading", await fixture.MerchantScalarAsync($"""
            SELECT restriction FROM public.account_license_restrictions WHERE account_id = '{account:D}'
            """));
    }

    private static async Task<string> RecoverAsync(P0213Fixture fixture, Guid shop,
        EntitlementSnapshotV1 trial, long before)
    {
        await DiscardSnapshotResponseAsync(fixture, trial, shop);
        var times = (string)(await EvidenceTimesAsync(fixture, trial.EventId))!;
        fixture.Transport.LeaseClock.Now += TimeSpan.FromSeconds(5);
        Assert.True(await fixture.DeliverAsync(source: true));
        Assert.Equal("AwaitingAcknowledgement", await OutboxAsync(fixture, trial.EventId));
        await DiscardAcknowledgementResponseAsync(fixture, trial);
        Assert.Equal(before + 1, await EvidenceCountAsync(fixture, shop));
        Assert.Equal(times, await EvidenceTimesAsync(fixture, trial.EventId));
        return times;
    }

    private async Task AssertOutageAsync(P0213Fixture fixture, BrowserRun run, CancellationToken ct)
    {
        var name = fixture.Hosts.Fixture.Runtime.Database!;
        await database.SetConnectionsAsync(name, allowed: false);
        try
        {
            await AssertBrowserPhaseAsync(fixture, run, "outage", ct);
        }
        finally
        {
            await database.SetConnectionsAsync(name, allowed: true);
        }
    }

    private async Task AssertBrowserPhaseAsync(P0213Fixture fixture, BrowserRun run, string phase, CancellationToken ct)
    {
        var before = (fixture.MerchantTraffic.Count, fixture.LicensingTraffic.Count);
        var start = new ProcessStartInfo("bun") { WorkingDirectory = run.Root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", "../../tests/licensing/browser/playwright.p0213.config.ts",
            "--grep", "@" + phase })
            start.ArgumentList.Add(arg);
        start.Environment["NODE_PATH"] = Path.Combine(run.Root, "node_modules");
        start.Environment["P0112_ADMIN_WEB"] = run.Admin;
        start.Environment["P0213_MERCHANT_WEB"] = run.Merchant;
        start.Environment["P0213_OWNER_EMAIL"] = run.OwnerEmail;
        start.Environment["P0213_STAFF_EMAIL"] = run.StaffEmail;
        start.Environment["P0213_MERCHANT_PASSWORD"] = P0201Tests.Password;
        start.Environment["P0112_PROOF_FILE"] = run.Proof;
        start.Environment["P0213_PROVIDER_STATE"] = run.ProviderState;
        start.Environment["P0213_ACCOUNT"] = run.Account.ToString("D");
        start.Environment["P0213_EVIDENCE"] = run.Evidence;
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await browser.Process.WaitForExitAsync(ct);
        var log = await stdout;
        output.WriteLine(log);
        Assert.True(browser.Process.ExitCode == 0 && log.Contains("1 passed", StringComparison.Ordinal)
            && File.Exists(Path.Combine(run.Evidence, $"observations-{phase}.json")), log + await stderr);
        Assert.Equal(before, (fixture.MerchantTraffic.Count, fixture.LicensingTraffic.Count));
    }

    private static async Task WriteEvidenceAsync(P0213Fixture fixture, BrowserRun run,
        EntitlementSnapshotV1 trial, Guid shop, long before, string times, CancellationToken ct)
    {
        var stock = await fixture.MerchantScalarAsync($"SELECT count(*) FROM public.inventory_batches WHERE shop_id = '{shop:D}'");
        Assert.Equal(4L, stock);
        Assert.Equal(before + 2, await EvidenceCountAsync(fixture, shop));
        Assert.Equal("Applied", await OutboxAsync(fixture, trial.EventId));
        Assert.Equal("Trading", await fixture.MerchantScalarAsync($"""
            SELECT a.restriction FROM public.shop_license_states s JOIN public.account_license_restrictions a USING (account_id)
            WHERE s.shop_id = '{shop:D}' AND enforcement_activated_at IS NULL AND state = 'Expired'
            """));
        using var parsed = JsonDocument.Parse(times);
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "postgres-evidence.json"), JsonSerializer.Serialize(new
        {
            provenance = "Local PostgreSQL, two Kestrel APIs and shipping web builds, fixture keys and dates",
            eventId = trial.EventId, trialStarts = P0210Fixture.Start, trialEnds = P0210Fixture.Start.AddDays(14),
            evidenceBeforeTrialDelivery = before, evidenceAfterRetry = before + 1, evidenceAfterBoundary = before + 2,
            snapshotResponseLostAfterCommit = true, acknowledgementResponseLostAfterCommit = true,
            acknowledgementInboxCount = 1, finalAcknowledgement = "Applied", originalTimestamps = parsed.RootElement,
            restriction = "Trading", enforcementActivatedAt = (DateTimeOffset?)null, stock,
            privateBrowserRequestsReachedApi = 0,
        }, Indented), ct);
    }

    private sealed record BrowserRun(string Root, string Evidence, string Admin, string Merchant,
        string OwnerEmail, string StaffEmail, Guid Account, string Proof, string ProviderState);
}

internal sealed class P0213BrowserFactAttribute : FactAttribute
{
    public P0213BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0213_RUN_BROWSER") != "1")
            Skip = "Set P0213_RUN_BROWSER=1 after building both shipping applications.";
    }
}
