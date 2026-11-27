using System.Net;
using System.Net.Http.Headers;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0206Tests
{
    [Fact]
    public async Task UnauthenticatedForeignAndForgedRequests_AreRefused()
    {
        await using var fixture = await P0206Fixture.StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using var _manager = manager;
        var planId = Guid.NewGuid();
        using (var noCsrf = await manager.SendAsync(HttpMethod.Put, $"{Plans}/{planId}/draft", MultiShop(0)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
            Assert.Equal("Admin.RequestRejected", await P0206Fixture.CodeAsync(noCsrf));
        }
        using (var foreign = await P0206Fixture.PutPlanAsync(manager, planId, MultiShop(0), "https://untrusted.example.test"))
            Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        using (var trialNoCsrf = await manager.SendAsync(HttpMethod.Put, Trial, new { expectedVersion = 0 }))
            Assert.Equal(HttpStatusCode.Forbidden, trialNoCsrf.StatusCode);
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM plans"));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM trial_policy_versions"));

        using var fixtureSession = new P0110CookieBrowser(P0114Tests.RawClient(fixture.Api));
        fixtureSession.Set("Intelibill.Admin.FixtureSession", P0206Fixture.FixtureToken);
        foreach (var path in new[] { Plans, Trial })
        {
            using var refused = await fixtureSession.SendAsync(HttpMethod.Get, path);
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }
        using var merchant = P0114Tests.RawClient(fixture.Api);
        var jwt = P0109Tests.CreateMerchantToken(fixture.Clock.GetUtcNow());
        merchant.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        merchant.DefaultRequestHeaders.Add("Cookie", "__Host-InteliBill.Admin.Access=" + jwt);
        using var bearer = await merchant.GetAsync(new Uri(Plans, UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, bearer.StatusCode);
    }

    [Fact]
    public async Task CapabilityChangesAndSessionExpiry_AreEnforcedLive()
    {
        await using var fixture = await P0206Fixture.StartAsync(database);
        string seed;
        using (var owner = await fixture.SignInOwnerAsync()) seed = await fixture.EnrollOperatorAsync(owner);
        await fixture.GrantAsync(P0114Tests.Invitee, "support");
        using (var support = await fixture.SignInOperatorAsync(seed))
        {
            using var denied = await support.SendAsync(HttpMethod.Get, Plans);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("Catalogue.AccessDenied", await P0206Fixture.CodeAsync(denied));
            using var write = await P0206Fixture.PutPlanAsync(support, Guid.NewGuid(), MultiShop(0));
            Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);

            await fixture.GrantAsync(P0114Tests.Invitee, P0206Fixture.Manager);
            using var ended = await support.SendAsync(HttpMethod.Get, Plans);
            Assert.Equal(HttpStatusCode.Unauthorized, ended.StatusCode);
        }

        using (var manager = await fixture.SignInOperatorAsync(seed))
        {
            await SaveAsync(manager, Guid.NewGuid(), MultiShop(0), HttpStatusCode.Created);
            await fixture.GrantAsync(P0114Tests.Invitee, "support");
            using var withdrawn = await manager.SendAsync(HttpMethod.Get, Plans);
            Assert.Equal(HttpStatusCode.Unauthorized, withdrawn.StatusCode);
        }
        using (var former = await fixture.SignInOperatorAsync(seed))
        {
            using var refused = await former.SendAsync(HttpMethod.Get, Plans);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        await fixture.GrantAsync(P0114Tests.Invitee, P0206Fixture.Manager);
        using var current = await fixture.SignInOperatorAsync(seed);
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        await SaveAsync(current, Guid.NewGuid(), MultiShop(0), HttpStatusCode.Created);
        fixture.Clock.Advance(TimeSpan.FromMinutes(9));
        using var expired = await P0206Fixture.PutPlanAsync(current, Guid.NewGuid(), MultiShop(0));
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal(2L, await fixture.ScalarAsync("SELECT count(*) FROM plans"));
    }
}
