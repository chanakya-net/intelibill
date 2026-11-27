using System.Net;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0208Tests
{
    [Fact]
    public async Task Retirement_RequiresLiveManagerRecentMfaAndExactRequest()
    {
        await using var fixture = await P0206Fixture.StartAsync(database, P0207Tests.LocalSettings());
        var (manager, seed) = await fixture.ManagerAsync();
        using var session = manager;
        var (plan, version, _, _) = await PublishedPlanAsync(manager);
        var key = Guid.NewGuid();
        var recovery = $"/api/admin/catalogue/retirements/{key}";

        await AssertInvalidRetirementRequestsAsync(manager, plan, version, key);
        fixture.Clock.Advance(TimeSpan.FromSeconds(299));
        using var accepted = await RetireAsync(manager, plan, version, key);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var initial = (await P0206Fixture.BodyAsync(accepted)).GetRawText();
        using var changed = await RetireAsync(manager, plan, version, key, reason: "Changed reason");
        await AssertErrorAsync(changed, HttpStatusCode.Conflict, "Catalogue.RetirementConflict");

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        using var expired = await RetireAsync(manager, plan, version, key);
        await AssertErrorAsync(expired, HttpStatusCode.Forbidden, "Catalogue.RecentMfaRequired");
        using var expiredRead = await manager.SendAsync(HttpMethod.Get, recovery);
        await AssertErrorAsync(expiredRead, HttpStatusCode.Forbidden, "Catalogue.RecentMfaRequired");
        using var fresh = await fixture.SignInOperatorAsync(seed);
        using var replay = await RetireAsync(fresh, plan, version, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(initial, (await P0206Fixture.BodyAsync(replay)).GetRawText());
        using var recovered = await fresh.SendAsync(HttpMethod.Get, recovery);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(initial, (await P0206Fixture.BodyAsync(recovered)).GetRawText());

        using var owner = await fixture.SignInOwnerAsync();
        using var denied = await RetireAsync(owner, plan, version, key);
        await AssertErrorAsync(denied, HttpStatusCode.Forbidden, "Catalogue.AccessDenied");
        var (other, _) = await fixture.ManagerAsync("retirement-other@example.test");
        using var otherSession = other;
        using var privateRecovery = await other.SendAsync(HttpMethod.Get, recovery);
        await AssertErrorAsync(privateRecovery, HttpStatusCode.NotFound, "Catalogue.RetirementNotFound");
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        await fixture.GrantAsync(P0114Tests.Invitee);
        using var revoked = await fresh.SendAsync(HttpMethod.Get, recovery);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM catalogue_retirements"));
    }

    [Fact]
    public async Task Retirement_AtMfaDeadlineWritesNothing()
    {
        await using var fixture = await P0206Fixture.StartAsync(database, P0207Tests.LocalSettings());
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        var (plan, version, _, _) = await PublishedPlanAsync(manager);
        fixture.Clock.Advance(TimeSpan.FromSeconds(300));
        using var expired = await RetireAsync(manager, plan, version, Guid.NewGuid());
        await AssertErrorAsync(expired, HttpStatusCode.Forbidden, "Catalogue.RecentMfaRequired");
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM catalogue_retirements"));
        Assert.Equal(2, await fixture.ScalarAsync($"SELECT status FROM plan_versions WHERE id = '{version}'"));
    }

    [Fact]
    public async Task RetirementRecovery_SessionSnapshotIdentifiesTheSameAdministratorAfterFreshMfa()
    {
        await using var fixture = await P0206Fixture.StartAsync(database, P0207Tests.LocalSettings());
        var (manager, seed) = await fixture.ManagerAsync();
        using var original = manager;
        var before = await SessionIdentityAsync(original);

        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        using var fresh = await fixture.SignInOperatorAsync(seed);
        var after = await SessionIdentityAsync(fresh);
        Assert.Equal(before.AdminId, after.AdminId);
        Assert.NotEqual(before.SessionId, after.SessionId);

        var (other, _) = await fixture.ManagerAsync("retirement-other@example.test");
        using var different = other;
        Assert.NotEqual(before.AdminId, (await SessionIdentityAsync(different)).AdminId);
    }

    private static async Task<(Guid AdminId, Guid SessionId)> SessionIdentityAsync(P0110CookieBrowser browser)
    {
        using var response = await browser.SendAsync(HttpMethod.Get, "/api/admin/auth/session");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var session = await P0206Fixture.BodyAsync(response);
        return (session.GetProperty("adminId").GetGuid(), session.GetProperty("sessionId").GetGuid());
    }

    private static async Task AssertInvalidRetirementRequestsAsync(P0110CookieBrowser manager, Guid plan, Guid version, Guid key)
    {
        var path = PlanPath(plan, version) + "/retire";
        var proof = await P0206Fixture.ProofAsync(manager);
        var body = new { operationKey = key, expectedVersion = 2, reason = Reason, confirmed = true };
        using var forged = await manager.SendAsync(HttpMethod.Post, path, body, proof, "https://untrusted.example.test");
        await AssertErrorAsync(forged, HttpStatusCode.Forbidden, "Admin.RequestRejected");
        using var missingProof = await manager.SendAsync(HttpMethod.Post, path, body);
        await AssertErrorAsync(missingProof, HttpStatusCode.Forbidden, "Admin.RequestRejected");

        object[] invalid =
        [
            body with { expectedVersion = 0 }, body with { confirmed = false }, body with { reason = " " },
            body with { reason = new string('x', 501) },
            new { body.operationKey, body.expectedVersion, body.reason, body.confirmed, unknown = true },
        ];
        foreach (var request in invalid)
        {
            using var refused = await manager.SendAsync(HttpMethod.Post, path, request, proof);
            await AssertErrorAsync(refused, HttpStatusCode.BadRequest, "Catalogue.RetirementInvalid");
        }

        using var stale = await RetireAsync(manager, plan, version, key, 1);
        await AssertErrorAsync(stale, HttpStatusCode.Conflict, "Catalogue.RetirementConflict");
        using var wrongVersion = await RetireAsync(manager, plan, Guid.NewGuid(), key);
        await AssertErrorAsync(wrongVersion, HttpStatusCode.Conflict, "Catalogue.RetirementConflict");
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, await P0206Fixture.CodeAsync(response));
    }
}
