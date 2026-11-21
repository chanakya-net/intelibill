using System.Net;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0417Tests
{
    [Fact]
    public async Task ReopeningRoutesRequireLicensingManagerRecentMfaAndAccountScope()
    {
        var clock = new P0201Clock { Now = P0202Fixture.WholeSecondNow() };
        await using var fixture = await P0213Fixture.StartAsync(database, businessClock: clock);
        var (owner, account, route) = await CloseAsync(fixture, clock.Now);
        AlignProviderClock(fixture);
        using var platform = await fixture.Trial.Catalogue.SignInOwnerAsync();
        await AssertDeniedAsync(platform, route);
        foreach (var capability in new[] { "support", "billing", "security-administrator" })
        {
            using var other = await SignInAsync(fixture, capability, platform);
            await AssertDeniedAsync(other, route);
        }

        using var admin = await SignInAsync(fixture, ownerSession: platform);
        var impact = await PreviewAsync(admin, route);
        var command = Command(impact, owner);
        using var foreign = await P0212Fixture.PostAsync(admin, route.Replace(account.ToString("D"), Guid.NewGuid().ToString("D")), command);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var noCsrf = await admin.SendAsync(HttpMethod.Post, route, command);
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        using var client = fixture.Trial.Catalogue.Api.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Session", Guid.NewGuid().ToString("D"));
        using var mismatched = admin.Fork(client);
        using var badSession = await P0212Fixture.PostAsync(mismatched, route, command);
        Assert.Equal(HttpStatusCode.Unauthorized, badSession.StatusCode);

        using var bare = await P0212Fixture.PostAsync(admin, route, command with { OwnerConsentEvidence = "" });
        Assert.Equal("Reopening.Invalid", await P0206Fixture.CodeAsync(bare));
        using var historical = await P0212Fixture.PostAsync(admin, route, command with { OwnerConsentAt = command.OwnerConsentAt.AddDays(-1) });
        Assert.Equal("Reopening.ConsentRequired", await P0206Fixture.CodeAsync(historical));
        Assert.Equal(0L, await fixture.Trial.CountAsync("operational_closure_reopenings"));

        using var own = await P0212Fixture.PostAsync(admin, route, command);
        Assert.True(own.StatusCode == HttpStatusCode.Created, await own.Content.ReadAsStringAsync());
        using var mismatch = await P0212Fixture.PostAsync(admin, route, command with { Reason = "Another decision" });
        Assert.Equal("Reopening.PayloadMismatch", await P0206Fixture.CodeAsync(mismatch));
        fixture.Trial.Catalogue.Clock.Advance(TimeSpan.FromMinutes(5));
        using var stale = await P0212Fixture.PostAsync(admin, route, command);
        Assert.Equal("Reopening.RecentMfaRequired", await P0206Fixture.CodeAsync(stale));
        await fixture.Trial.Catalogue.GrantAsync("reopening-licensing-manager@example.test", "support");
        using var revoked = await P0212Fixture.PostAsync(admin, route, command);
        Assert.True(revoked.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"A revoked session must be denied before replay; observed {(int)revoked.StatusCode}.");
        Assert.Equal(1L, await fixture.Trial.CountAsync("operational_closure_reopenings"));
    }

    private static async Task AssertDeniedAsync(P0110CookieBrowser admin, string route)
    {
        using var preview = await P0212Fixture.PostAsync(admin, route + "/preview", new { });
        Assert.Equal("Reopening.AccessDenied", await P0206Fixture.CodeAsync(preview));
        using var save = await P0212Fixture.PostAsync(admin, route, new { });
        Assert.Equal("Reopening.AccessDenied", await P0206Fixture.CodeAsync(save));
    }

    [Fact]
    public async Task ConcurrentReopeningsSaveOnce()
    {
        var clock = new P0201Clock { Now = P0202Fixture.WholeSecondNow() };
        await using var fixture = await P0213Fixture.StartAsync(database, businessClock: clock);
        var (owner, _, route) = await CloseAsync(fixture, clock.Now);
        var closed = (await fixture.LatestSnapshotAsync(owner.ShopId)).Shop!;
        using var admin = await SignInAsync(fixture);
        var command = Command(await PreviewAsync(admin, route), owner);
        using var otherSession = admin.Fork(fixture.Trial.Catalogue.Api.CreateClient());
        var replies = await Task.WhenAll(P0212Fixture.PostAsync(admin, route, command), P0212Fixture.PostAsync(otherSession, route, command));
        using var created = Assert.Single(replies, response => response.StatusCode == HttpStatusCode.Created);
        using var replay = Assert.Single(replies, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Equal(await created.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(closed.Revision + 1, (await fixture.LatestSnapshotAsync(owner.ShopId)).Shop!.Revision);
        Assert.Equal(1L, await fixture.Trial.CountAsync("operational_closure_reopenings"));
    }

    [Fact]
    public async Task RecloseAfterReopeningStartsANewLedgerVersion()
    {
        var clock = new P0201Clock { Now = P0202Fixture.WholeSecondNow() };
        await using var fixture = await P0213Fixture.StartAsync(database, businessClock: clock);
        var (owner, account, route) = await CloseAsync(fixture, clock.Now);
        await fixture.DrainAsync(source: true);
        using var admin = await SignInAsync(fixture);
        var old = Command(await PreviewAsync(admin, route), owner);
        using var opened = await P0212Fixture.PostAsync(admin, route, old);
        Assert.True(opened.StatusCode == HttpStatusCode.Created, await opened.Content.ReadAsStringAsync());
        await fixture.DrainAsync(source: true);

        var reclosed = await CloseShopAsync(owner, account);
        Assert.NotEqual(route, reclosed);
        var impact = await PreviewAsync(admin, reclosed);
        Assert.Equal(3, impact.GetProperty("version").GetInt64());
        using var stale = await P0212Fixture.PostAsync(admin, reclosed, old with { OperationKey = Guid.NewGuid() });
        Assert.Equal("Reopening.VersionConflict", await P0206Fixture.CodeAsync(stale));
        using var reused = await P0212Fixture.PostAsync(admin, reclosed, Command(impact, owner));
        Assert.Equal("Reopening.ConsentRequired", await P0206Fixture.CodeAsync(reused));
        using var fresh = await P0212Fixture.PostAsync(admin, reclosed, Command(impact, owner) with { OwnerConsentReference = "REVIEW-898-002" });
        Assert.True(fresh.StatusCode == HttpStatusCode.Created, await fresh.Content.ReadAsStringAsync());
        Assert.Equal(2L, await fixture.Trial.CountAsync("operational_closure_reopenings"));
        Assert.Equal(2L, await fixture.Trial.CountAsync("operational_closure_targets"));
    }

    private static async Task AssertAuditCannotBeChangedAsync(P0213Fixture fixture, string statement)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, statement));
        Assert.Equal("55000", error.SqlState);
    }
}
