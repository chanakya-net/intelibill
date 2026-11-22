using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0417Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task ReviewedReopeningRestoresTheExistingTermWithImmutableEvidence()
    {
        var clock = new P0201Clock { Now = P0202Fixture.WholeSecondNow() };
        await using var fixture = await P0213Fixture.StartAsync(database, businessClock: clock);
        var (owner, account, route) = await CloseAsync(fixture, clock.Now);
        var closed = (await fixture.LatestSnapshotAsync(owner.ShopId)).Shop!;
        Assert.Equal("Closed", closed.State);
        using var admin = await SignInAsync(fixture);
        var impact = await PreviewAsync(admin, route);
        var command = Command(impact, owner);
        using var saved = await P0212Fixture.PostAsync(admin, route, command);
        Assert.True(saved.StatusCode == HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        using var replay = await P0212Fixture.PostAsync(admin, route, command);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await saved.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        var reopened = (await fixture.LatestSnapshotAsync(owner.ShopId)).Shop!;
        Assert.Equal("Active", reopened.State);
        Assert.Equal(closed.TermId, reopened.TermId);
        Assert.Equal(closed.StartsAt, reopened.StartsAt);
        Assert.Equal(closed.AccessEndsAt, reopened.AccessEndsAt);
        Assert.Equal(closed.Revision, reopened.ReopenedRevision);
        Assert.Equal(1L, await fixture.Trial.CountAsync("operational_closure_reopenings"));
        Assert.Equal(command.OwnerConsentReference, await fixture.SourceScalarAsync(
            $"SELECT owner_consent_reference FROM operational_closure_reopenings WHERE account_id = '{account:D}'"));
        Assert.Equal("ReopenShop", await fixture.SourceScalarAsync($"""
            SELECT owner_consent_action FROM operational_closure_reopenings
            WHERE shop_id = '{owner.ShopId:D}' AND owner_consent_user_id = '{owner.UserId:D}'
                AND owner_consent_evidence = '{command.OwnerConsentEvidence}' AND reason = '{command.Reason}'
                AND reopened_by IS NOT NULL AND session_id IS NOT NULL AND mfa_verified_at <= reopened_at
            """));
        var trial = await owner.Client.GetFromJsonAsync<Intelibill.Shared.Contracts.V1.ShopTrialPreviewV1>(P0210Tests.Path(owner.ShopId));
        Assert.False(trial!.Eligible);
        Assert.Equal(1L, await fixture.Trial.CountAsync("subscription_coverage_terms"));
        Assert.Equal(0L, await fixture.Trial.CountAsync("trial_history"));
        foreach (var sql in new[] { "UPDATE operational_closure_reopenings SET reason = 'changed'",
            "DELETE FROM operational_closure_reopenings", "TRUNCATE operational_closure_reopenings" })
            await AssertAuditCannotBeChangedAsync(fixture, sql);
    }

    internal static async Task<(Merchant Owner, Guid Account, string Route)> CloseAsync(
        P0213Fixture fixture, DateTimeOffset at, bool paid = true, bool till = false)
    {
        var (owner, account) = await OpenShopAsync(fixture, at, paid);
        if (till) await P0403Tests.OpenTillAsync(fixture, owner);
        return (owner, account, await CloseShopAsync(owner, account));
    }

    private static async Task<(Merchant Owner, Guid Account)> OpenShopAsync(P0213Fixture fixture, DateTimeOffset at, bool paid = true)
    {
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        var anchor = FirstOfMonth(at);
        Guid? term = paid ? await SeedTermAsync(fixture.Hosts, account, new TermSeed(anchor.ToUniversalTime(),
            anchor.AddMonths(1).ToUniversalTime(), AnchorLocal: FormattableString.Invariant($"{anchor:yyyy-MM-dd HH:mm:ss}"))) : null;
        await P0213Fixture.LinkAsync(account, owner, owner, term);
        await fixture.DrainAsync(source: true);
        return (owner, account);
    }

    private static DateTimeOffset FirstOfMonth(DateTimeOffset at)
    {
        var local = at.ToOffset(TimeSpan.FromHours(5.5));
        return new(local.Year, local.Month, 1, 0, 0, 0, local.Offset);
    }

    private static string ClosurePath(Merchant owner) => $"/api/licensing/shops/{owner.ShopId:D}/closure";

    private static async Task<string> CloseShopAsync(Merchant owner, Guid account) =>
        ReopeningRoute(owner, account, await SaveClosureAsync(owner, await CloseCommandAsync(owner, ClosurePath(owner))));

    private static async Task<JsonElement> SaveClosureAsync(Merchant owner,
        Intelibill.Shared.Contracts.V1.ShopClosureConfirmationV1 command)
    {
        using var closed = await owner.Client.PostAsJsonAsync(ClosurePath(owner), command);
        Assert.True(closed.IsSuccessStatusCode, await closed.Content.ReadAsStringAsync());
        return await closed.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string ReopeningRoute(Merchant owner, Guid account, JsonElement closure) =>
        $"/api/admin/accounts/{account:D}/closures/{closure.GetProperty("id").GetGuid():D}/shops/{owner.ShopId:D}/reopening";

    private static async Task<Intelibill.Shared.Contracts.V1.ShopClosureConfirmationV1> CloseCommandAsync(Merchant owner, string path)
    {
        var review = await owner.Client.GetFromJsonAsync<JsonElement>(path + "-preview");
        return new(Guid.NewGuid(), review.GetProperty("closureVersion").GetInt64(), review.GetProperty("ownerVersion").GetInt64(),
            review.GetProperty("workDigest").GetString()!, true, true, "NoFinancialChange");
    }

    internal static async Task<P0110CookieBrowser> SignInAsync(P0213Fixture fixture, string capability = "licensing-manager",
        P0110CookieBrowser? ownerSession = null)
    {
        var catalogue = fixture.Trial.Catalogue;
        AlignProviderClock(fixture);
        using var ownedPlatform = ownerSession is null ? await catalogue.SignInOwnerAsync() : null;
        var platform = ownerSession ?? ownedPlatform!;
        var email = $"reopening-{capability}@example.test";
        var seed = await catalogue.EnrollOperatorAsync(platform, email);
        await P0206Fixture.GrantAsync(platform, email, capability);
        var admin = await catalogue.SignInOperatorAsync(seed, email);
        // Invitation and TOTP helpers advance the provider clock. Drain only once that outbox work is due.
        if (fixture.Transport.LeaseClock.Now < catalogue.Clock.GetUtcNow())
            fixture.Transport.LeaseClock.Now = catalogue.Clock.GetUtcNow();
        return admin;
    }

    private static void AlignProviderClock(P0213Fixture fixture)
    {
        // Closure and provider console share wall time in production. Their fixture hosts use separate clocks.
        var clock = fixture.Trial.Catalogue.Clock;
        var offset = fixture.AuthClock.GetUtcNow() - clock.GetUtcNow();
        if (offset > TimeSpan.Zero) clock.Advance(offset);
    }

    internal static async Task<JsonElement> PreviewAsync(P0110CookieBrowser admin, string route)
    {
        using var preview = await P0212Fixture.PostAsync(admin, route + "/preview", new { });
        Assert.True(preview.StatusCode == HttpStatusCode.OK,
            $"Expected reopening preview (200), got {(int)preview.StatusCode}: {await preview.Content.ReadAsStringAsync()}");
        return await preview.Content.ReadFromJsonAsync<JsonElement>();
    }

    internal static ReopenRequest Command(JsonElement impact, Merchant owner) =>
        new("Reviewed existing paid coverage", true, "REVIEW-898-001", owner.UserId, impact.GetProperty("closedAt").GetDateTimeOffset().AddTicks(10),
            "Owner explicitly asked to reopen this specific shop.", impact.GetProperty("version").GetInt64(),
            impact.GetProperty("impactDigest").GetString()!, Guid.NewGuid());

    internal sealed record ReopenRequest(string Reason, bool OwnerConsentConfirmed, string OwnerConsentReference,
        Guid OwnerConsentUserId, DateTimeOffset OwnerConsentAt, string OwnerConsentEvidence,
        long ExpectedVersion, string ImpactDigest, Guid OperationKey);
}
