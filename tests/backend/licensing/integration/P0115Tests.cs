using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Licensing.Application.AdminIdentities;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

// API host startup configures Serilog's process-wide logger.
[Collection(LicensingDatabaseTests.Name)]
public sealed class P0115Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Owner = "p0108.owner@example.test";
    private const string SignInContext = "/api/admin/auth/sign-in/context";
    private const string Session = "/api/admin/auth/session";
    private const string Refresh = "/api/admin/auth/refresh";
    private const string Users = "/api/admin/users";
    private const string Audit = "/api/admin/audit";

    [Fact]
    public async Task SignInRequiresLocalKeyAndBothFactorsUnderTheRestrictedDatabaseRole()
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        var settings = P0108Tests.SignInMfaSettings();
        AddSigningRoot(settings);
        await using var api = new LicensingApiFactory(runtime, clock: clock, settings: settings);
        var (anonymousClient, password, seed) = await P0108Tests.EnrollSignInOwnerAsync(api, clock);
        using var unsigned = new P0110CookieBrowser(anonymousClient);
        await AssertUnkeyedSignInRefusedAsync(runtime, clock, settings);
        await AssertStatusAsync(unsigned, Session, HttpStatusCode.Unauthorized);
        using var passwordOnly = await unsigned.SendAsync(HttpMethod.Post, "/api/admin/auth/sign-in",
            new { email = Owner, password }, await ProofAsync(unsigned));
        Assert.Equal(HttpStatusCode.Unauthorized, passwordOnly.StatusCode);
        using var signedIn = await P0114Tests.SignInAsync(api, clock, Owner, password, seed);
        await AssertStatusAsync(signedIn, Users, HttpStatusCode.OK);
        Assert.DoesNotContain("billing", await GrantsAsync(signedIn));
        await AssertDatabaseIsolationAsync(runtime);
        clock.Advance(TimeSpan.FromMinutes(15));
        await AssertStatusAsync(signedIn, Session, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InvitationAndDelegationPreserveAuthorityBoundaries()
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        var settings = P0108Tests.SignInMfaSettings();
        AddSigningRoot(settings);
        var mailer = new P0114Tests.FixtureMailer();
        await using var api = new LicensingApiFactory(runtime, clock: clock, settings: settings,
            configure: services => services.AddSingleton<IAdminInvitationMailer>(mailer));
        var (client, password, seed) = await P0108Tests.EnrollSignInOwnerAsync(api, clock);
        client.Dispose();
        using var owner = await P0114Tests.SignInAsync(api, clock, Owner, password, seed);
        var operatorSeed = await EnrollOperatorAsync(api, clock, owner, mailer);
        using var oldSession = await P0114Tests.SignInAsync(api, clock, P0114Tests.Invitee,
            P0114Tests.InviteePassword, operatorSeed);
        Assert.Empty(await GrantsAsync(oldSession));

        await GrantSupportAsync(owner);
        await AssertStatusAsync(oldSession, Session, HttpStatusCode.Unauthorized);
        using var refresh = await oldSession.SendAsync(HttpMethod.Post, Refresh, csrf: await ProofAsync(oldSession));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        clock.Advance(TimeSpan.FromSeconds(30));
        using var current = await P0114Tests.SignInAsync(api, clock, P0114Tests.Invitee,
            P0114Tests.InviteePassword, operatorSeed);
        Assert.Equal("support", Assert.Single(await GrantsAsync(current)));
        await AssertStatusAsync(current, Audit, HttpStatusCode.Forbidden);
        await AssertStatusAsync(owner, Audit, HttpStatusCode.OK);
    }

    private static async Task AssertUnkeyedSignInRefusedAsync(NpgsqlConnectionStringBuilder runtime,
        P0106Tests.TestClock clock, Dictionary<string, string?> settings)
    {
        // The existing bootstrap supplies a valid origin to both hosts.
        var unkeyed = new Dictionary<string, string?>(settings) { ["AdminSessions:SigningKey"] = null };
        await using var api = new LicensingApiFactory(runtime, clock: clock, settings: unkeyed);
        using var browser = new P0110CookieBrowser(P0114Tests.RawClient(api));
        using var response = await browser.SendAsync(HttpMethod.Get, SignInContext);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("Admin.SignInUnavailable", (await BodyAsync(response)).GetProperty("code").GetString());
    }

    private async Task AssertDatabaseIsolationAsync(NpgsqlConnectionStringBuilder runtime)
    {
        var merchant = database.Login(runtime.Database!, LicensingPostgreSqlFixture.MerchantRole);
        var provider = database.Login(LicensingPostgreSqlFixture.MerchantDatabase, LicensingPostgreSqlFixture.RuntimeRole);
        foreach (var otherDatabase in new[] { merchant, provider })
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                LicensingPostgreSqlFixture.ReadScalarAsync(otherDatabase, "SELECT current_database()"));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }
        Assert.Equal(1L, await LicensingPostgreSqlFixture.ReadScalarAsync(runtime,
            "SELECT count(*) FROM initial_admin_bootstrap WHERE owner_id IS NOT NULL"));
    }

    private static async Task<string> EnrollOperatorAsync(LicensingApiFactory api, P0106Tests.TestClock clock,
        P0110CookieBrowser owner, P0114Tests.FixtureMailer mailer)
    {
        await P0114Tests.IssueAsync(owner, P0114Tests.Invitee);
        using var enrollment = P0114Tests.EnrollmentClient(api);
        using var accepted = await P0114Tests.AcceptAsync(enrollment, mailer.Token);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var denied = await enrollment.GetAsync(new Uri("/api/admin/access", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        return await P0114Tests.CompleteEnrollmentAsync(enrollment, clock);
    }

    private static async Task GrantSupportAsync(P0110CookieBrowser owner)
    {
        using var users = await owner.SendAsync(HttpMethod.Get, Users);
        Assert.Equal(HttpStatusCode.OK, users.StatusCode);
        var target = Assert.Single((await BodyAsync(users)).GetProperty("items").EnumerateArray(),
            item => string.Equals(item.GetProperty("email").GetString(), P0114Tests.Invitee, StringComparison.OrdinalIgnoreCase));
        var path = Users + "/" + target.GetProperty("id").GetGuid() + "/capabilities";
        var update = new
        {
            capabilities = new[] { "support" },
            expectedVersion = target.GetProperty("version").GetUInt32(),
            reason = "responsibilities-changed",
            confirmed = true,
        };
        using var missing = await owner.SendAsync(HttpMethod.Put, path, update);
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        using var wrongOrigin = await owner.SendAsync(HttpMethod.Put, path, update,
            await ProofAsync(owner), "https://other.example.test");
        Assert.Equal(HttpStatusCode.Forbidden, wrongOrigin.StatusCode);
        using var saved = await owner.SendAsync(HttpMethod.Put, path, update, await ProofAsync(owner));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private static void AddSigningRoot(Dictionary<string, string?> settings) =>
        settings["AdminSessions:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static async Task<string?[]> GrantsAsync(P0110CookieBrowser browser)
    {
        using var response = await browser.SendAsync(HttpMethod.Get, Session);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await BodyAsync(response)).GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()).ToArray();
    }

    private static async Task<string?> ProofAsync(P0110CookieBrowser browser)
    {
        using var response = await browser.SendAsync(HttpMethod.Get, SignInContext);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("requestToken").GetString();
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task AssertStatusAsync(P0110CookieBrowser browser, string path, HttpStatusCode status)
    {
        using var response = await browser.SendAsync(HttpMethod.Get, path);
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }
}
