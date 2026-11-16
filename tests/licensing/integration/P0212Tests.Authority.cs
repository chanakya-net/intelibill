using System.Net;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0212Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0212Tests
{
    [Fact]
    public async Task OnlyALiveLicensingManagerWithRecentMfaCanGrantOrRecover()
    {
        await using var fixture = await StartAsync(database);
        var (account, _, shops) = await fixture.SeedAccountAsync(1);
        string seed;
        using (var owner = await fixture.Provider.SignInOwnerAsync())
        {
            // Platform Owner alone grants nothing, before any receipt can be disclosed.
            await AssertDeniedAsync(owner, account, shops, HttpStatusCode.Forbidden, "Grant.AccessDenied");
            seed = await fixture.Provider.EnrollOperatorAsync(owner);
        }

        // Support, Billing and Security Administrator together still grant nothing here.
        await fixture.Provider.GrantAsync(P0114Tests.Invitee, "support", "billing", "security-administrator");
        using (var other = await fixture.Provider.SignInOperatorAsync(seed))
            await AssertDeniedAsync(other, account, shops, HttpStatusCode.Forbidden, "Grant.AccessDenied");

        await fixture.Provider.GrantAsync(P0114Tests.Invitee, P0206Fixture.Manager);
        using var manager = await fixture.Provider.SignInOperatorAsync(seed);
        var preview = await PreviewAsync(manager, account, Draft(shops));
        var key = Guid.NewGuid();

        // Transport: origin, CSRF, pinned session and the fixture scheme are all refused without facts.
        using (var foreignOrigin = await PostAsync(manager, Base(account), Confirm(preview, key), "https://evil.example"))
            Assert.Equal(HttpStatusCode.Forbidden, foreignOrigin.StatusCode);
        using (var noCsrf = await manager.SendAsync(HttpMethod.Post, Base(account), Confirm(preview, key)))
            Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        using (var fixtureToken = await FixtureClientPostAsync(fixture, account, Confirm(preview, key)))
            Assert.Equal(HttpStatusCode.Unauthorized, fixtureToken.StatusCode);
        Assert.Equal(0L, await fixture.CountAsync("SELECT count(*) FROM complimentary_grants"));

        // Recent MFA: exactly five minutes after sign-in the write and recovery are refused.
        fixture.Provider.Clock.Advance(TimeSpan.FromMinutes(5));
        using (var stale = await PostAsync(manager, Base(account), Confirm(preview, key)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
            Assert.Equal("Grant.RecentMfaRequired", await P0206Fixture.CodeAsync(stale));
        }

        using var fresh = await fixture.Provider.SignInOperatorAsync(seed);
        var grant = await GrantAsync(fresh, account, Draft(shops), key);
        using (var staleRecovery = await AfterMfaWindowAsync(fixture, fresh, $"{Base(account)}/operations/{key:D}"))
            Assert.Equal("Grant.RecentMfaRequired", await P0206Fixture.CodeAsync(staleRecovery));
        using var again = await fixture.Provider.SignInOperatorAsync(seed);
        using (var recovered = await again.SendAsync(HttpMethod.Get, $"{Base(account)}/operations/{key:D}"))
            Assert.Equal(grant.GetRawText(), (await P0206Fixture.BodyAsync(recovered)).GetRawText());

        // Removing the capability ends grant and recovery for the same administrator.
        await fixture.Provider.GrantAsync(P0114Tests.Invitee, "support");
        using var demoted = await fixture.Provider.SignInOperatorAsync(seed);
        using (var lost = await demoted.SendAsync(HttpMethod.Get, $"{Base(account)}/operations/{key:D}"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, lost.StatusCode);
            Assert.DoesNotContain("Launch partner", await lost.Content.ReadAsStringAsync());
        }
        Assert.Equal(1L, await fixture.CountAsync("SELECT count(*) FROM complimentary_grants"));
    }

    [Fact]
    public async Task ReplayIsExactAndChangedPayloadsOrStalePreviewsConflict()
    {
        await using var fixture = await StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        var (account, _, shops) = await fixture.SeedAccountAsync(2);
        var first = await PreviewAsync(manager, account, Draft([shops[0]]));
        var stalePreview = await PreviewAsync(manager, account, Draft([shops[1]]));
        var key = Guid.NewGuid();
        using (var created = await PostAsync(manager, Base(account), Confirm(first, key)))
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // Same key, changed facts: a conflict that discloses nothing about the original.
        using (var changed = await PostAsync(manager, Base(account), Confirm(stalePreview, key)))
        {
            Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
            Assert.Equal("Grant.PayloadMismatch", await P0206Fixture.CodeAsync(changed));
        }

        // A preview made before another grant committed can no longer be confirmed under a new key.
        using (var stale = await PostAsync(manager, Base(account), Confirm(stalePreview, Guid.NewGuid())))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("Grant.VersionConflict", await P0206Fixture.CodeAsync(stale));
        }

        // A committed replay is returned even after the grant has ended and the revision moved on.
        await GrantAsync(manager, account, Draft([shops[1]]));
        fixture.Lifecycle.Now = Ends.AddDays(1);
        using var replay = await PostAsync(manager, Base(account), Confirm(first, key));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(1, (await P0206Fixture.BodyAsync(replay)).GetProperty("grantedVersion").GetInt64());
        using (var unknown = await manager.SendAsync(HttpMethod.Get, $"{Base(account)}/operations/{Guid.NewGuid():D}"))
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using (var otherAccount = await manager.SendAsync(HttpMethod.Get, $"{Base(Guid.NewGuid())}/operations/{key:D}"))
            Assert.Equal(HttpStatusCode.NotFound, otherAccount.StatusCode);
        Assert.Equal(2L, await fixture.CountAsync("SELECT count(*) FROM complimentary_grants"));
    }

    [Fact]
    public async Task AnotherAdministratorCannotRecoverOrReuseTheOperation()
    {
        await using var fixture = await StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        var (account, _, shops) = await fixture.SeedAccountAsync(1);
        var key = Guid.NewGuid();
        var preview = await PreviewAsync(manager, account, Draft(shops));
        using (var created = await PostAsync(manager, Base(account), Confirm(preview, key)))
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        const string second = "second.manager@example.test";
        using (var owner = await fixture.Provider.SignInOwnerAsync())
        {
            var seed = await fixture.Provider.EnrollOperatorAsync(owner, second);
            await fixture.Provider.GrantAsync(second, P0206Fixture.Manager);
            using var colleague = await fixture.Provider.SignInOperatorAsync(seed, second);
            using var recovery = await colleague.SendAsync(HttpMethod.Get, $"{Base(account)}/operations/{key:D}");
            Assert.Equal(HttpStatusCode.NotFound, recovery.StatusCode);
            using var reuse = await PostAsync(colleague, Base(account), Confirm(preview, key));
            Assert.Equal(HttpStatusCode.Conflict, reuse.StatusCode);
            Assert.Equal("Grant.PayloadMismatch", await P0206Fixture.CodeAsync(reuse));
            Assert.DoesNotContain("Launch partner", await reuse.Content.ReadAsStringAsync());
        }
        Assert.Equal(1L, await fixture.CountAsync("SELECT count(*) FROM complimentary_grant_receipts"));
    }

    private static async Task AssertDeniedAsync(P0110CookieBrowser browser, Guid account, Guid[] shops,
        HttpStatusCode status, string code)
    {
        using var preview = await PostAsync(browser, Base(account) + "/preview", Draft(shops));
        Assert.Equal(status, preview.StatusCode);
        Assert.Equal(code, await P0206Fixture.CodeAsync(preview));
        using var write = await PostAsync(browser, Base(account), new
        {
            operationKey = Guid.NewGuid(), shopIds = shops, startsAt = "2027-03-01T00:00:00Z", endsAt = "2027-04-01T00:00:00Z",
            memberAllowance = 2, reason = "Denied", expectedVersion = 0, sourceHash = new string('a', 64), confirmed = true,
        });
        Assert.Equal(status, write.StatusCode);
        using var recovery = await browser.SendAsync(HttpMethod.Get, $"{Base(account)}/operations/{Guid.NewGuid():D}");
        Assert.Equal(status, recovery.StatusCode);
    }

    private static async Task<HttpResponseMessage> AfterMfaWindowAsync(P0212Fixture fixture, P0110CookieBrowser browser, string path)
    {
        fixture.Provider.Clock.Advance(TimeSpan.FromMinutes(5));
        var response = await browser.SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        return response;
    }

    /// <summary>The local fixture session and a merchant bearer are never provider grant sessions.</summary>
    private static async Task<HttpResponseMessage> FixtureClientPostAsync(P0212Fixture fixture, Guid account, object body)
    {
        using (var merchant = P0114Tests.RawClient(fixture.Provider.Api))
        {
            var jwt = P0109Tests.CreateMerchantToken(fixture.Provider.Clock.GetUtcNow());
            merchant.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);
            merchant.DefaultRequestHeaders.Add("Cookie", "__Host-InteliBill.Admin.Access=" + jwt);
            using var refused = await merchant.GetAsync(new Uri($"{Base(account)}/operations/{Guid.NewGuid():D}", UriKind.Relative));
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }

        using var browser = new P0110CookieBrowser(P0114Tests.RawClient(fixture.Provider.Api));
        browser.Set("Intelibill.Admin.FixtureSession", P0206Fixture.FixtureToken);
        return await browser.SendAsync(HttpMethod.Post, Base(account), body);
    }
}
