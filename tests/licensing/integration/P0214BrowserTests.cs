using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0213Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0214BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [P0214BrowserFact]
    [Trait("Category", "Browser")]
    public async Task BothScreensShowSavedVersusAppliedThroughALostAcknowledgement()
    {
        var evidence = Environment.GetEnvironmentVariable("P0214_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence), "Set P0214_EVIDENCE_DIR for browser observations.");
        Directory.CreateDirectory(evidence);
        await using var fixture = await P0213Fixture.StartAsync(database, browser: true);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var staff = await SignInAsync(fixture.Hosts, ShopRole.Staff, owner.ShopId);
        var account = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(account, owner, owner);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        await EntitlementBrowserRun.WithWebAsync(fixture, owner, staff, account, evidence, "p0214", output,
            (run, ct) => ExerciseAsync(fixture, run, owner, ct));
    }

    private async Task ExerciseAsync(P0213Fixture fixture, EntitlementBrowserRun run, Merchant owner, CancellationToken ct)
    {
        await run.AssertPhaseAsync(fixture, "prepare", ct);
        var policy = (Guid)(await fixture.SourceScalarAsync("SELECT id FROM public.trial_policy_versions WHERE status = 2"))!;
        var revision = (int)(await fixture.SourceScalarAsync($"SELECT revision FROM public.trial_policy_versions WHERE id = '{policy:D}'"))!;
        fixture.Transport.LeaseClock.Now = P0202Fixture.WholeSecondNow().AddMinutes(1);
        var acceptance = new ShopTrialAcceptanceV1(Guid.NewGuid(), policy, revision, "trial-local-1", true);
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(owner, acceptance)).Status);

        var (trial, applied) = await DemonstrateRecoveryAsync(fixture, run, owner.ShopId, ct);
        var (unresolved, converged) = await DemonstrateAttentionAsync(fixture, run, owner.ShopId, applied, ct);
        var name = fixture.Hosts.Fixture.Runtime.Database!;
        await database.SetConnectionsAsync(name, allowed: false);
        try { await run.AssertPhaseAsync(fixture, "outage", ct); }
        finally { await database.SetConnectionsAsync(name, allowed: true); }
        await WriteEvidenceAsync(fixture, run, trial, applied, unresolved, converged, ct);
    }

    private static async Task<(EntitlementSnapshotV1 Trial, EntitlementSynchronizationV1 Applied)> DemonstrateRecoveryAsync(
        P0213Fixture fixture, EntitlementBrowserRun run, Guid shop, CancellationToken ct)
    {
        var trial = await fixture.LatestSnapshotAsync(shop);
        var before = await EvidenceCountAsync(fixture, shop);
        var saved = await ObserveAsync(fixture, run, "saved", ct);
        Assert.Equal(trial.Shop!.Revision, saved.SavedShopRevision);
        Assert.Equal(trial.Shop.Revision - 1, saved.AppliedShopRevision);
        Assert.Equal(1L, before);

        await fixture.DrainAsync(source: true);
        await P0214Tests.LoseAcknowledgementAsync(fixture, trial);
        var unacknowledged = await ObserveAsync(fixture, run, "unacknowledged", ct);
        Assert.Equal(saved with { Delivery = "AwaitingAcknowledgement" }, unacknowledged);
        Assert.Equal(before + 1, await EvidenceCountAsync(fixture, shop));
        var times = (string)(await EvidenceTimesAsync(fixture, trial.EventId))!;

        fixture.Transport.LeaseClock.Now += TimeSpan.FromSeconds(4);
        Assert.True(await fixture.DeliverAsync(source: true));
        await DiscardAcknowledgementResponseAsync(fixture, trial, retrySeconds: 8);
        var applied = await ObserveAsync(fixture, run, "applied", ct);
        using var json = JsonDocument.Parse(times);
        Assert.Equal(json.RootElement.GetProperty("appliedAt").GetDateTimeOffset(), applied.LastSynchronizedAt);
        Assert.Equal(trial.Shop.UpstreamVerifiedAt, applied.SavedVerifiedAt);
        Assert.Equal(trial.Account!.Revision, applied.AppliedAccountRevision);
        Assert.Equal(trial.Shop.Revision, applied.AppliedShopRevision);
        Assert.Equal(before + 1, await EvidenceCountAsync(fixture, shop));
        return (trial, applied);
    }

    private static async Task<(EntitlementSynchronizationV1 Unresolved, EntitlementSynchronizationV1 Converged)> DemonstrateAttentionAsync(
        P0213Fixture fixture, EntitlementBrowserRun run, Guid shop, EntitlementSynchronizationV1 applied, CancellationToken ct)
    {
        fixture.Hosts.StatusClock.Now += TimeSpan.FromHours(47);
        await fixture.ScanAsync(source: true);
        await P0214Tests.ExhaustSnapshotsAsync(fixture);
        var unresolved = await ObserveAsync(fixture, run, "needs-review", ct);
        Assert.Equal(applied.AppliedAccountRevision, unresolved.AppliedAccountRevision);
        Assert.Equal(applied.AppliedShopRevision, unresolved.AppliedShopRevision);
        Assert.Equal(applied.LastSynchronizedAt, unresolved.LastSynchronizedAt);
        Assert.True(unresolved.SavedAccountRevision > applied.SavedAccountRevision);
        Assert.True(unresolved.SavedShopRevision > applied.SavedShopRevision);

        fixture.Hosts.StatusClock.Now += TimeSpan.FromHours(47);
        await fixture.ScanAsync(source: true);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        var converged = await ObserveAsync(fixture, run, "converged", ct);
        Assert.True(converged.AppliedShopRevision > unresolved.SavedShopRevision);
        Assert.True(converged.AppliedAccountRevision > unresolved.SavedAccountRevision);
        Assert.Equal(3L, await EvidenceCountAsync(fixture, shop));
        return (unresolved, converged);
    }

    private static async Task<EntitlementSynchronizationV1> ObserveAsync(
        P0213Fixture fixture, EntitlementBrowserRun run, string phase, CancellationToken ct)
    {
        await run.AssertPhaseAsync(fixture, phase, ct);
        using var observed = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(run.Evidence, $"observations-{phase}.json"), ct));
        return observed.RootElement.GetProperty("synchronization").Deserialize<EntitlementSynchronizationV1>()!;
    }

    private static async Task WriteEvidenceAsync(P0213Fixture fixture, EntitlementBrowserRun run, EntitlementSnapshotV1 trial,
        EntitlementSynchronizationV1 applied, EntitlementSynchronizationV1 unresolved, EntitlementSynchronizationV1 converged,
        CancellationToken ct)
    {
        var shop = trial.ShopId;
        var stock = await fixture.MerchantScalarAsync($"SELECT count(*) FROM inventory_batches WHERE shop_id = '{shop:D}'");
        Assert.Equal(6L, stock);
        Assert.Equal(1L, await fixture.MerchantScalarAsync(
            $"SELECT count(*) FROM shop_license_states WHERE shop_id = '{shop:D}' AND enforcement_activated_at IS NULL"));
        using var times = JsonDocument.Parse((string)(await EvidenceTimesAsync(fixture, trial.EventId))!);
        var json = JsonSerializer.Serialize(new
        {
            provenance = "Local PostgreSQL, two Kestrel APIs and shipping Chromium builds, fixture trial policy and keys",
            trialEvent = trial.EventId,
            firstSuccessfulSynchronization = applied,
            unresolved,
            converged,
            originalTrialTimestamps = times.RootElement,
            stock,
            enforcementActivatedAt = (DateTimeOffset?)null,
            privateBrowserRequestsReachedApi = 0,
        }, Indented);
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "postgres-evidence.json"), json, ct);
    }
}

internal sealed class P0214BrowserFactAttribute : FactAttribute
{
    public P0214BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0214_RUN_BROWSER") != "1")
            Skip = "Set P0214_RUN_BROWSER=1 after building both shipping applications.";
    }
}
