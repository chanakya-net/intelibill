using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Intelibill.Licensing.Application.AdminIdentities;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0111Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Sessions = "/api/admin/sessions";
    private const string Session = "/api/admin/auth/session";
    private const string Refresh = "/api/admin/auth/refresh";
    private const string SignOut = "/api/admin/auth/sign-out";
    private const string AccessCookie = "__Host-InteliBill.Admin.Access";
    private const string RefreshCookie = "__Host-InteliBill.Admin.Refresh";

    private static readonly string[] SafeFields = ["absoluteExpiresAt", "createdAt", "id", "isCurrent", "lastSeenAt"];

    [Fact]
    public async Task ListsOnlyOwnLiveSessionsWithSafeMetadata()
    {
        await using var fixture = await StartAsync();
        using var owner = await fixture.OwnerAsync();
        using var ordinary = await fixture.InviteeAsync(owner);
        using var secondOwner = await fixture.OwnerAsync();
        var otherOwnerId = await fixture.LatestSessionAsync();
        using var list = await GetAsync(ordinary, Sessions, HttpStatusCode.OK);
        var row = Assert.Single(list.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(SafeFields,
            row.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
        Assert.True(row.GetProperty("isCurrent").GetBoolean());
        Assert.NotEqual(otherOwnerId, row.GetProperty("id").GetGuid());
        Assert.Equal(row.GetProperty("createdAt").GetDateTimeOffset().AddHours(12),
            row.GetProperty("absoluteExpiresAt").GetDateTimeOffset());
        using var scopeProbe = await GetAsync(owner, Sessions + "?adminId=" + row.GetProperty("id").GetGuid(), HttpStatusCode.BadRequest);
        await AssertOwnerPagesAsync(owner);
        await AssertOtherCredentialsDeniedAsync(fixture);
        Assert.Equal(3L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action = 17"));
    }

    [Fact]
    public async Task RevokingOneDeviceEndsItsAccessAndRefreshOnly()
    {
        await using var fixture = await StartAsync();
        using var actor = await fixture.OwnerAsync();
        using var target = await fixture.OwnerAsync();
        var targetId = await fixture.LatestSessionAsync();
        using var root = target.Fork(P0114Tests.RawClient(fixture.Api));
        using var sibling = await fixture.OwnerAsync();
        using var ordinary = await fixture.InviteeAsync(actor);
        var foreignId = await fixture.LatestSessionAsync();
        using var rotated = await PostAsync(target, Refresh, HttpStatusCode.OK);
        using var refused = await actor.SendAsync(HttpMethod.Post, Revoke(targetId), new { reason = "device-unused" });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using var wrongOrigin = await PostAsync(actor, Revoke(targetId), HttpStatusCode.Forbidden, "https://other.example.test");
        using var foreign = await PostAsync(ordinary, Revoke(targetId), HttpStatusCode.NotFound);
        using var missing = await PostAsync(ordinary, Revoke(Guid.NewGuid()), HttpStatusCode.NotFound);
        using var ownerDenied = await PostAsync(actor, Revoke(foreignId), HttpStatusCode.NotFound);
        using var foreignBody = JsonDocument.Parse(await foreign.Content.ReadAsStringAsync());
        using var missingBody = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
        Assert.Equal(foreignBody.RootElement.GetProperty("code").GetString(), missingBody.RootElement.GetProperty("code").GetString());
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM admin_sessions WHERE revoked_at IS NOT NULL"));
        using var revoked = await PostAsync(actor, Revoke(targetId), HttpStatusCode.NoContent);
        using var repeated = await PostAsync(actor, Revoke(targetId), HttpStatusCode.NoContent);
        Assert.False(revoked.Headers.Contains("Set-Cookie"));
        await AssertEndedAsync(root);
        await AssertEndedAsync(target);
        await AssertSurvivesAsync(sibling);
        await AssertSurvivesAsync(ordinary);
        Assert.Equal(1L, await fixture.ScalarAsync($"""
            SELECT count(*) FROM provider_audit_events WHERE action = 18
            AND "after"->>'TargetId' = '{targetId}'
            AND "before"->>'Version' <> "after"->>'Version'
            """));
    }

    [Fact]
    public async Task SignOutEndsCurrentFamilyAfterAccessExpiry()
    {
        await using var fixture = await StartAsync();
        using var sibling = await fixture.OwnerAsync();
        using var target = await fixture.OwnerAsync();
        using var saved = target.Fork(P0114Tests.RawClient(fixture.Api));
        using var siblingRefresh = await PostAsync(sibling, Refresh, HttpStatusCode.OK);
        fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        using var expired = await GetAsync(target, Session, HttpStatusCode.Unauthorized);
        using var noCsrf = await target.SendAsync(HttpMethod.Post, SignOut);
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        using var wrongOrigin = await PostAsync(target, SignOut, HttpStatusCode.Forbidden, "https://other.example.test");
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM admin_sessions WHERE revoked_at IS NOT NULL"));
        using var ended = await PostAsync(target, SignOut, HttpStatusCode.NoContent);
        Assert.Null(target[AccessCookie]);
        Assert.Null(target[RefreshCookie]);
        await AssertEndedAsync(saved);
        using var repeat = await PostAsync(target, SignOut, HttpStatusCode.NoContent);
        using var renewSibling = await PostAsync(sibling, Refresh, HttpStatusCode.OK);
        using var active = await GetAsync(sibling, Session, HttpStatusCode.OK);
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action = 19"));
    }

    private static async Task AssertOwnerPagesAsync(P0110CookieBrowser owner)
    {
        using var first = await GetAsync(owner, Sessions + "?pageSize=1", HttpStatusCode.OK);
        var firstRow = Assert.Single(first.RootElement.GetProperty("items").EnumerateArray());
        Assert.False(firstRow.GetProperty("isCurrent").GetBoolean());
        var cursor = Uri.EscapeDataString(first.RootElement.GetProperty("nextCursor").GetString()!);
        using var second = await GetAsync(owner, Sessions + "?pageSize=1&cursor=" + cursor, HttpStatusCode.OK);
        var secondRow = Assert.Single(second.RootElement.GetProperty("items").EnumerateArray());
        Assert.True(secondRow.GetProperty("isCurrent").GetBoolean());
        Assert.Equal(JsonValueKind.Null, second.RootElement.GetProperty("nextCursor").ValueKind);
    }

    private static async Task AssertOtherCredentialsDeniedAsync(Fixture fixture)
    {
        using var anonymous = new P0110CookieBrowser(P0114Tests.RawClient(fixture.Api));
        using var refused = await GetAsync(anonymous, Sessions, HttpStatusCode.Unauthorized);
        anonymous.Set("Intelibill.Admin.FixtureSession", "p0111-fixture-only");
        using var fixtureRefused = await GetAsync(anonymous, Sessions, HttpStatusCode.Unauthorized);
        using var merchant = P0114Tests.RawClient(fixture.Api);
        var token = P0109Tests.CreateMerchantToken(fixture.Clock.GetUtcNow());
        merchant.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        merchant.DefaultRequestHeaders.Add("Cookie", AccessCookie + "=" + token);
        using var merchantRefused = await merchant.GetAsync(new Uri(Sessions, UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, merchantRefused.StatusCode);
    }

    private static string Revoke(Guid id) => $"{Sessions}/{id}/revoke";

    private static async Task AssertEndedAsync(P0110CookieBrowser browser)
    {
        using var access = await GetAsync(browser, Session, HttpStatusCode.Unauthorized);
        using var refresh = await PostAsync(browser, Refresh, HttpStatusCode.Unauthorized);
    }

    private static async Task AssertSurvivesAsync(P0110CookieBrowser browser)
    {
        using var access = await GetAsync(browser, Session, HttpStatusCode.OK);
        using var refresh = await PostAsync(browser, Refresh, HttpStatusCode.OK);
    }

    private static async Task<JsonDocument> GetAsync(P0110CookieBrowser browser, string path, HttpStatusCode status)
    {
        using var response = await browser.SendAsync(HttpMethod.Get, path);
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body.Length == 0 ? "{}" : body);
    }

    private static async Task<HttpResponseMessage> PostAsync(P0110CookieBrowser browser, string path, HttpStatusCode status,
        string origin = P0110CookieBrowser.Origin)
    {
        using var context = await GetAsync(browser, "/api/admin/auth/sign-in/context", HttpStatusCode.OK);
        var response = await browser.SendAsync(HttpMethod.Post, path, path.StartsWith(Sessions, StringComparison.Ordinal)
            ? new { reason = "device-unused" } : null, context.RootElement.GetProperty("requestToken").GetString(), origin);
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return response;
    }

    private async Task<Fixture> StartAsync()
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        var settings = P0108Tests.SignInMfaSettings();
        settings["AdminSessions:SigningKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        settings["AdminFixtures:SessionDigest"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("p0111-fixture-only"u8));
        settings["AdminFixtures:AdminId"] = Guid.NewGuid().ToString();
        var mailer = new P0114Tests.FixtureMailer();
        var api = new LicensingApiFactory(runtime, clock: clock, settings: settings,
            configure: services => services.AddSingleton<IAdminInvitationMailer>(mailer));
        var (enrollment, password, seed) = await P0108Tests.EnrollSignInOwnerAsync(api, clock);
        enrollment.Dispose();
        return new(runtime, clock, settings, api, mailer, password, seed);
    }

    private sealed record Fixture(NpgsqlConnectionStringBuilder Runtime, P0106Tests.TestClock Clock,
        Dictionary<string, string?> Settings, LicensingApiFactory Api, P0114Tests.FixtureMailer Mailer,
        string Password, string Seed) : IAsyncDisposable
    {
        internal async Task<P0110CookieBrowser> OwnerAsync()
        {
            Clock.Advance(TimeSpan.FromSeconds(30));
            return await P0114Tests.SignInAsync(Api, Clock, "p0108.owner@example.test", Password, Seed);
        }

        internal async Task<P0110CookieBrowser> InviteeAsync(P0110CookieBrowser owner)
        {
            await P0114Tests.IssueAsync(owner, P0114Tests.Invitee);
            using var enrollment = P0114Tests.EnrollmentClient(Api);
            using var accepted = await P0114Tests.AcceptAsync(enrollment, Mailer.Token);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            using var denied = await enrollment.GetAsync(new Uri(Sessions, UriKind.Relative));
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            var seed = await P0114Tests.CompleteEnrollmentAsync(enrollment, Clock);
            return await P0114Tests.SignInAsync(Api, Clock, P0114Tests.Invitee, P0114Tests.InviteePassword, seed);
        }

        internal async Task<Guid> LatestSessionAsync() =>
            (Guid)(await ScalarAsync("SELECT id FROM admin_sessions ORDER BY created_at DESC LIMIT 1"))!;

        internal Task<object?> ScalarAsync(string sql) => LicensingPostgreSqlFixture.ReadScalarAsync(Runtime, sql);

        public ValueTask DisposeAsync() => Api.DisposeAsync();
    }
}
