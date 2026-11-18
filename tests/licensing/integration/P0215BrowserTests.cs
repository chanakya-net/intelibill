using System.Text.Json;
using Intelibill.Domain.Enums;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0215Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0215BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [P0215BrowserFact]
    [Trait("Category", "Browser")]
    public async Task BothBuiltConsumersShowAcceptedCancellationAndExactExpiry()
    {
        var evidence = Environment.GetEnvironmentVariable("P0215_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence));
        Directory.CreateDirectory(evidence);
        await using var fixture = await P0213Fixture.StartAsync(database, browser: true);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var second = await CreateTradingShopAsync(fixture.Hosts);
        var distinct = await CreateTradingShopAsync(fixture.Hosts);
        var staff = await SignInAsync(fixture.Hosts, ShopRole.Staff, owner.ShopId);
        var account = await CreateAccountAsync(owner.Client);
        var otherAccount = await CreateAccountAsync(owner.Client, "Separate fixture billing account");
        var (term, end) = await SeedCurrentTermAsync(fixture, account, capacity: 2);
        var (otherTerm, _) = await SeedCurrentTermAsync(fixture, account);
        await P0213Fixture.LinkAsync(account, owner, owner, term);
        await P0213Fixture.LinkAsync(account, owner, second, term);
        await P0213Fixture.LinkAsync(account, owner, distinct, otherTerm);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        await File.WriteAllTextAsync(Path.Combine(evidence, "fixture.json"), JsonSerializer.Serialize(new
        {
            term, end, otherTerm, otherAccount, affectedShops = new[] { owner.ShopId, second.ShopId }, distinctShop = distinct.ShopId,
        }, Json));

        await EntitlementBrowserRun.WithWebAsync(fixture, owner, staff, account, evidence, "p0215", output,
            (run, ct) => ExerciseAsync(fixture, run, owner, distinct, end, ct));
    }

    private async Task ExerciseAsync(P0213Fixture fixture, EntitlementBrowserRun run, Merchant owner, Merchant distinct,
        DateTimeOffset end, CancellationToken ct)
    {
        await run.AssertPhaseAsync(fixture, "prepare", ct);
        var other = await fixture.LatestSnapshotAsync(distinct.ShopId);
        await run.AssertPhaseAsync(fixture, "schedule", ct);
        await run.AssertPhaseAsync(fixture, "pending", ct);
        var snapshot = await fixture.LatestSnapshotAsync(owner.ShopId);
        Assert.Equal(end, snapshot.Shop!.AccessEndsAt);
        Assert.Equal(end, snapshot.Shop.NextBoundary);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        await run.AssertPhaseAsync(fixture, "applied", ct);
        fixture.Hosts.StatusClock.Now = end.AddTicks(-10);
        await run.AssertPhaseAsync(fixture, "before-end", ct);
        fixture.Hosts.StatusClock.Now = end;
        await run.AssertPhaseAsync(fixture, "exact-end", ct);
        Assert.Equal(other, await fixture.LatestSnapshotAsync(distinct.ShopId));
        await WriteDatabaseEvidenceAsync(fixture, run, owner, ct);
        await run.AssertPhaseAsync(fixture, "support", ct);
        var name = fixture.Hosts.Fixture.Runtime.Database!;
        await database.SetConnectionsAsync(name, allowed: false);
        try { await run.AssertPhaseAsync(fixture, "outage", ct); }
        finally { await database.SetConnectionsAsync(name, allowed: true); }
    }

    private static async Task WriteDatabaseEvidenceAsync(P0213Fixture fixture, EntitlementBrowserRun run, Merchant owner,
        CancellationToken ct)
    {
        var facts = (string)(await fixture.SourceScalarAsync("""
            SELECT jsonb_build_object('term', t.term_id, 'paidThrough', t.ends_at, 'basis', f.basis, 'pricePaise', f.price_paise,
                'effectiveAt', c.effective_at, 'changeId', c.change_id, 'acceptedShops', c.shop_count,
                'receipts', (SELECT count(*) FROM subscription_cancellation_receipts))::text
            FROM scheduled_subscription_changes c
            JOIN subscription_coverage_terms t USING (account_id, term_id)
            JOIN subscription_term_facts f USING (account_id, term_id)
            """))!;
        var observeOnly = await fixture.MerchantScalarAsync($"""
            SELECT count(*) FROM shop_license_states WHERE shop_id = '{owner.ShopId:D}' AND enforcement_activated_at IS NULL
            """);
        Assert.Equal(1L, observeOnly);
        using var raw = JsonDocument.Parse(facts);
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "postgres-evidence.json"),
            JsonSerializer.Serialize(new { facts = raw.RootElement, enforcementActivatedAt = (DateTimeOffset?)null }, Json), ct);
    }
}

internal sealed class P0215BrowserFactAttribute : FactAttribute
{
    public P0215BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0215_RUN_BROWSER") != "1")
            Skip = "Set P0215_RUN_BROWSER=1 after building both applications.";
    }
}
