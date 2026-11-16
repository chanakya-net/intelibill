using System.Text.Json;
using Intelibill.Domain.Enums;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0215Fixture;

namespace Intelibill.Licensing.Integration.Tests;

[Collection(MerchantBillingHosts.Name)]
public sealed class P0216BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [P0216BrowserFact]
    [Trait("Category", "Browser")]
    public async Task BothBuiltConsumersShowWithdrawalAndOriginalGrace()
    {
        var evidence = Environment.GetEnvironmentVariable("P0216_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence));
        Directory.CreateDirectory(evidence);
        await using var fixture = await P0213Fixture.StartAsync(database, browser: true);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var distinct = await CreateTradingShopAsync(fixture.Hosts);
        var staff = await SignInAsync(fixture.Hosts, ShopRole.Staff, owner.ShopId);
        var account = await CreateAccountAsync(owner.Client);
        var (term, end) = await SeedCurrentTermAsync(fixture, account);
        var (otherTerm, _) = await SeedCurrentTermAsync(fixture, account);
        await P0213Fixture.LinkAsync(account, owner, owner, term);
        await P0213Fixture.LinkAsync(account, owner, distinct, otherTerm);
        var paidBefore = await PaidFactsAsync(fixture.Hosts, term);
        var cancellation = await P0216Tests.ScheduleAsync(owner, account, term);
        var otherCancellation = await P0216Tests.ScheduleAsync(owner, account, otherTerm);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        await File.WriteAllTextAsync(Path.Combine(evidence, "fixture.json"), JsonSerializer.Serialize(new
        {
            note = "Local fixtures only; no live provider, payment or enforcement is exercised.",
            term, end, graceEnds = end.AddDays(7), otherTerm, cancellation, otherCancellation,
            shops = new { withdrawn = owner.ShopId, stillCancelled = distinct.ShopId },
        }, Json));

        await EntitlementBrowserRun.WithWebAsync(fixture, owner, staff, account, evidence, "p0216", output,
            (run, ct) => ExerciseAsync(fixture, run, owner, account, term, end, paidBefore, ct));
    }

    private static async Task ExerciseAsync(P0213Fixture fixture, EntitlementBrowserRun run, Merchant owner, Guid account,
        Guid term, DateTimeOffset end, string paidBefore, CancellationToken ct)
    {
        await run.AssertPhaseAsync(fixture, "prepare", ct);
        var scheduled = await fixture.LatestSnapshotAsync(owner.ShopId);
        Assert.Equal(end, scheduled.Shop!.AccessEndsAt);
        await run.AssertPhaseAsync(fixture, "withdraw", ct);
        var withdrawn = await fixture.LatestSnapshotAsync(owner.ShopId);
        Assert.Equal(scheduled.Shop.Revision + 1, withdrawn.Shop!.Revision);
        Assert.Equal(end.AddDays(7), withdrawn.Shop.AccessEndsAt);
        await run.AssertPhaseAsync(fixture, "pending", ct);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        await run.AssertPhaseAsync(fixture, "applied", ct);
        fixture.Hosts.StatusClock.Now = end;
        await run.AssertPhaseAsync(fixture, "exact-end", ct);
        await WriteDatabaseEvidenceAsync(fixture, run, owner, account, term, paidBefore, ct);
    }

    private static async Task WriteDatabaseEvidenceAsync(P0213Fixture fixture, EntitlementBrowserRun run, Merchant owner,
        Guid account, Guid term, string paidBefore, CancellationToken ct)
    {
        var history = (string)(await fixture.SourceScalarAsync("""
            SELECT jsonb_agg(jsonb_build_object('term', term_id, 'revision', revision, 'kind', kind, 'effectiveAt', effective_at,
                'recordedAt', recorded_at, 'withdraws', withdraws_change_id, 'changeId', change_id) ORDER BY term_id, revision)::text
            FROM scheduled_subscription_changes
            """))!;
        var head = (string)(await fixture.SourceScalarAsync($"""
            SELECT to_jsonb(h)::text FROM entitlement_account_heads h WHERE account_id = '{account:D}'
            """))!;
        Assert.Equal(paidBefore, await PaidFactsAsync(fixture.Hosts, term));
        Assert.Equal(1L, await fixture.MerchantScalarAsync($"""
            SELECT count(*) FROM shop_license_states WHERE shop_id = '{owner.ShopId:D}' AND enforcement_activated_at IS NULL
            """));
        using var changes = JsonDocument.Parse(history);
        using var accountHead = JsonDocument.Parse(head);
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "postgres-evidence.json"), JsonSerializer.Serialize(new
        {
            changeHistory = changes.RootElement, receipts = await fixture.Trial.CountAsync("subscription_cancellation_receipts"),
            paidFactsEqualBeforeAndAfter = true, accountHead = accountHead.RootElement, enforcementActivatedAt = (DateTimeOffset?)null,
        }, Json), ct);
    }

    private static Task<string> PaidFactsAsync(Hosts hosts, Guid term) => P0216Tests.PaidFactsAsync(hosts, term);
}

internal sealed class P0216BrowserFactAttribute : FactAttribute
{
    public P0216BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0216_RUN_BROWSER") != "1")
            Skip = "Set P0216_RUN_BROWSER=1 after building both applications.";
    }
}
