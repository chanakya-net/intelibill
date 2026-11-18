using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0210BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string BrowserCommand = "bun x playwright test --config ../../tests/licensing/browser/playwright.p0210.config.ts";

    [P0210BrowserFact]
    [Trait("Category", "Browser")]
    public async Task OwnerStartsAndRecoversTrialWithoutAutomaticCharge()
    {
        await using var fixture = await P0210Fixture.StartAsync(database, browser: true);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var branch = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(owner.Client);
        await fixture.LinkAsync(account, owner.UserId, owner);
        await fixture.LinkAsync(account, owner.UserId, branch);
        Assert.Equal(0L, await fixture.CountAsync("trial_history"));
        var authority = await MerchantMembershipsAsync();

        var evidence = Environment.GetEnvironmentVariable("P0210_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence), "Set P0210_EVIDENCE_DIR for the required browser observations.");
        Directory.CreateDirectory(evidence);
        var root = Path.Combine(ProjectPaths.Root, "src/frontend");
        using var staged = new MerchantWebBuild(root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        await using var web = await StartMerchantWebAsync(staged.Root, owner.Client.BaseAddress!, timeout.Token);
        var run = new BrowserRun(root, web.Origin, evidence, owner.Email, branch.Email);
        await RunAsync(run, "activation", timeout.Token);

        var trial = await AssertPersistedAsync(fixture, run, owner, branch);
        fixture.Hosts.StatusClock.Now = trial.EndsAt;
        await RunAsync(run, "expired", timeout.Token);
        await P0209Tests.PostStockAsync(owner.Client, "Observed expired trial stock");
        Assert.Equal(authority, await MerchantMembershipsAsync());
        await RecordEvidenceAsync(fixture, run, trial, owner.ShopId, timeout.Token);
    }

    private static async Task<ShopTrialOperationV1> AssertPersistedAsync(P0210Fixture fixture, BrowserRun run, Merchant owner, Merchant branch)
    {
        using var observations = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(run.Evidence, "observations.json")));
        var browserTrial = observations.RootElement.GetProperty("applied").Deserialize<ShopTrialOperationV1>(Json)!;
        using var replay = await owner.Client.PostAsJsonAsync(P0210Tests.Path(owner.ShopId), fixture.Accept(browserTrial.OperationKey));
        Assert.Equal(browserTrial, await replay.Content.ReadFromJsonAsync<ShopTrialOperationV1>());
        Assert.Equal(P0210Fixture.Start.AddDays(14), browserTrial.EndsAt);
        Assert.Equal(1L, await fixture.CountAsync("trial_history"));
        Assert.Equal(1L, await fixture.CountAsync("subscription_coverage_terms"));
        Assert.Equal(0L, await fixture.CountAsync("subscription_term_facts"));
        Assert.Equal(2L, await fixture.CountAsync("shop_trial_inbox"));

        var history = (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.LicensingOwner, $"""
            SELECT json_build_object('acceptedAt', h.starts_at, 'appliedAt', h.applied_at, 'endsAt', h.ends_at,
                'memberCap', h.member_cap, 'accountTrialCap', h.account_trial_cap, 'termsVersion', h.terms_version,
                'operationKey', h.operation_key, 'policyId', h.policy_id, 'policyRevision', h.policy_revision,
                'activeMembers', h.active_members, 'developmentFixture', h.development_fixture)::text
            FROM trial_history h WHERE h.shop_id = '{owner.ShopId:D}'
            """))!;
        using var facts = JsonDocument.Parse(history);
        Assert.Equal(browserTrial.OperationKey, facts.RootElement.GetProperty("operationKey").GetGuid());
        Assert.Equal(browserTrial.EndsAt, facts.RootElement.GetProperty("endsAt").GetDateTimeOffset());
        Assert.Equal(5, facts.RootElement.GetProperty("memberCap").GetInt32());
        Assert.Equal(1, facts.RootElement.GetProperty("accountTrialCap").GetInt32());
        Assert.Equal(1L, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.LicensingOwner, $"""
            SELECT count(*) FROM shop_trial_inbox WHERE shop_id = '{branch.ShopId:D}' AND reason = 'AccountCap' AND term_id IS NULL
            """));
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "trial-facts.json"), history);
        return browserTrial;
    }

    private async Task RecordEvidenceAsync(P0210Fixture fixture, BrowserRun run, ShopTrialOperationV1 trial,
        Guid shop, CancellationToken ct)
    {
        var merchant = database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase);
        var consents = await LicensingPostgreSqlFixture.ReadScalarAsync(merchant, "SELECT count(*) FROM shop_trial_consents");
        var receipts = await LicensingPostgreSqlFixture.ReadScalarAsync(merchant, "SELECT count(*) FROM shop_trial_receipts");
        var stock = await LicensingPostgreSqlFixture.ReadScalarAsync(merchant,
            $"SELECT count(*) FROM inventory_batches WHERE shop_id = '{shop:D}'");
        Assert.Equal(2L, consents);
        Assert.Equal(2L, receipts);
        Assert.Equal(1L, stock);
        using var facts = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(run.Evidence, "trial-facts.json"), ct));
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "postgres-evidence.json"), JsonSerializer.Serialize(new
        {
            trial = facts.RootElement, replayEndsAt = trial.EndsAt, history = await fixture.CountAsync("trial_history"),
            coverageTerms = await fixture.CountAsync("subscription_coverage_terms"), deniedGrants = 0,
            outbox = consents, inbox = await fixture.CountAsync("shop_trial_inbox"), receipts,
            unchangedMemberships = true, observeOnlyStockBatches = stock, commercialTermFacts = 0,
        }, Json), ct);
        File.Delete(Path.Combine(run.Evidence, "trial-facts.json"));
        await RecordBuildAsync(run, fixture, ct);
    }

    private static async Task RecordBuildAsync(BrowserRun run, P0210Fixture fixture, CancellationToken ct)
    {
        using var ngsw = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(run.Root, "dist/INVENTORY/browser/ngsw.json"), ct));
        var git = new ProcessStartInfo("git") { WorkingDirectory = ProjectPaths.Root, RedirectStandardOutput = true };
        git.ArgumentList.Add("rev-parse");
        git.ArgumentList.Add("HEAD");
        await using var process = new OwnedProcess(git);
        var sha = (await process.Process.StandardOutput.ReadToEndAsync(ct)).Trim();
        await process.Process.WaitForExitAsync(ct);
        Assert.Equal(0, process.Process.ExitCode);
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "build-evidence.json"), JsonSerializer.Serialize(new
        {
            sha, buildId = ngsw.RootElement.GetProperty("appData").GetProperty("buildId").GetString(),
            fixture.PolicyId, fixture.PolicyRevision, command = BrowserCommand,
        }, Json), ct);
    }

    private async Task<string> MerchantMembershipsAsync() =>
        (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase),
            """
            SELECT jsonb_agg(jsonb_build_array(id, shop_id, user_id, role, status, authority_version) ORDER BY id)::text
            FROM shop_memberships
            """))!;

    private async Task RunAsync(BrowserRun run, string phase, CancellationToken ct)
    {
        var start = new ProcessStartInfo("bun") { WorkingDirectory = run.Root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", "../../tests/licensing/browser/playwright.p0210.config.ts", "--grep", "@" + phase })
            start.ArgumentList.Add(arg);
        start.Environment["NODE_PATH"] = Path.Combine(run.Root, "node_modules");
        start.Environment["P0210_MERCHANT_WEB"] = run.Origin;
        start.Environment["P0210_OWNER"] = run.Owner;
        start.Environment["P0210_BRANCH"] = run.Branch;
        start.Environment["P0210_PASSWORD"] = P0201Tests.Password;
        start.Environment["P0210_EVIDENCE_DIR"] = run.Evidence;
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout;
        output.WriteLine(report);
        var raw = Path.Combine(run.Evidence, "playwright");
        if (Directory.Exists(raw)) Directory.Delete(raw, recursive: true);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal), report + await stderr);
    }

    private sealed record BrowserRun(string Root, string Origin, string Evidence, string Owner, string Branch);
}

internal sealed class P0210BrowserFactAttribute : FactAttribute
{
    public P0210BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0210_RUN_BROWSER") != "1")
            Skip = "Set P0210_RUN_BROWSER=1 after building the shipping merchant application.";
    }
}
