using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Xunit.Abstractions;
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

        await EntitlementBrowserRun.WithWebAsync(fixture, owner, staff, account, evidence, "p0213", output,
            (run, ct) => PrepareAndDemonstrateAsync(fixture, run, owner, before, ct));
    }

    private async Task PrepareAndDemonstrateAsync(P0213Fixture fixture, EntitlementBrowserRun run, Merchant owner, long before, CancellationToken ct)
    {
        await run.AssertPhaseAsync(fixture, "prepare", ct);
        var policy = (Guid)(await fixture.SourceScalarAsync("SELECT id FROM public.trial_policy_versions WHERE status = 2"))!;
        var revision = (int)(await fixture.SourceScalarAsync($"SELECT revision FROM public.trial_policy_versions WHERE id = '{policy:D}'"))!;
        fixture.Transport.LeaseClock.Now = P0202Fixture.WholeSecondNow().AddMinutes(1);
        var acceptance = new ShopTrialAcceptanceV1(Guid.NewGuid(), policy, revision, "trial-local-1", true);
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(owner, acceptance)).Status);
        var trial = await fixture.LatestSnapshotAsync(owner.ShopId);
        Assert.Equal("Pending", await OutboxAsync(fixture, trial.EventId));
        await run.AssertPhaseAsync(fixture, "delayed", ct);
        Assert.Equal(before, await EvidenceCountAsync(fixture, owner.ShopId));

        var times = await RecoverAsync(fixture, owner.ShopId, trial, before);
        await run.AssertPhaseAsync(fixture, "recovered", ct);
        await AssertOutageAsync(fixture, run, ct);
        fixture.Hosts.StatusClock.Now = P0210Fixture.Start.AddDays(14);
        fixture.Transport.LeaseClock.Now = P0202Fixture.WholeSecondNow().AddMinutes(1);
        await fixture.ScanAsync(source: true);
        await fixture.DrainAsync(source: true);
        await run.AssertPhaseAsync(fixture, "expired", ct);
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

    private async Task AssertOutageAsync(P0213Fixture fixture, EntitlementBrowserRun run, CancellationToken ct)
    {
        var name = fixture.Hosts.Fixture.Runtime.Database!;
        await database.SetConnectionsAsync(name, allowed: false);
        try
        {
            await run.AssertPhaseAsync(fixture, "outage", ct);
        }
        finally
        {
            await database.SetConnectionsAsync(name, allowed: true);
        }
    }


    private static async Task WriteEvidenceAsync(P0213Fixture fixture, EntitlementBrowserRun run,
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

}

internal sealed class P0213BrowserFactAttribute : FactAttribute
{
    public P0213BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0213_RUN_BROWSER") != "1")
            Skip = "Set P0213_RUN_BROWSER=1 after building both shipping applications.";
    }
}
