using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Intelibill.Licensing.Application.AdminIdentities;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0114Tests
{
    private const string Capability = "admin.audit.identity.read";
    private const string FixtureCookie = "Intelibill.Admin.FixtureSession";
    private const string FixtureToken = "p0114-fixture-only";
    private const string Session = "/api/admin/auth/session";

    [Fact]
    public async Task LiveCapabilityAndSafeDenialEvidence()
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        var settings = Settings();
        settings["AdminFixtures:SessionDigest"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FixtureToken)));
        settings["AdminFixtures:AdminId"] = Guid.NewGuid().ToString();
        settings["AdminFixtures:Capabilities"] = Capability;
        var mailer = new FixtureMailer();
        await using var api = new LicensingApiFactory(runtime, clock: clock, settings: settings,
            configure: services => services.AddSingleton<IAdminInvitationMailer>(mailer));
        using var owner = await SignInOwnerAsync(api, clock);
        using var grants = await ReadAsync(owner, Session);
        Assert.Contains(Capability, grants.RootElement.GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()));
        await IssueAsync(owner, Invitee);
        using var enrollment = EnrollmentClient(api);
        using var accepted = await AcceptAsync(enrollment, mailer.Token);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var enrollmentOnly = await enrollment.GetAsync(new Uri(Audit, UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, enrollmentOnly.StatusCode);
        var seed = await CompleteEnrollmentAsync(enrollment, clock);
        using var invitee = await SignInAsync(api, clock, Invitee, InviteePassword, seed);
        using var inviteeGrants = await ReadAsync(invitee, Session);
        Assert.Empty(inviteeGrants.RootElement.GetProperty("capabilities").EnumerateArray());
        await AssertScopedDenialsAsync(runtime, owner, invitee);
        await AssertUnauthenticatedAsync(api, runtime, owner, invitee, clock);
    }

    internal static async Task<string> CompleteEnrollmentAsync(HttpClient enrollment, P0106Tests.TestClock clock)
    {
        using var started = await PostEnrollmentAsync(enrollment, Enrollment, Enrollment + "/start", new { });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        using var setup = await started.Content.ReadFromJsonAsync<JsonDocument>();
        var seed = setup!.RootElement.GetProperty("setupSecret").GetString()!;
        using var verified = await PostEnrollmentAsync(enrollment, Enrollment, Enrollment + "/verify",
            new { otp = P0108Tests.SignInOtp(seed, clock.GetUtcNow()) });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        clock.Advance(TimeSpan.FromSeconds(30));
        return seed;
    }

    private static async Task AssertScopedDenialsAsync(NpgsqlConnectionStringBuilder runtime,
        P0110CookieBrowser owner, P0110CookieBrowser invitee)
    {
        var migrator = new NpgsqlConnectionStringBuilder(runtime.ConnectionString) { Username = "licensing_migrator" };
        await ExecuteAsync(migrator, "REVOKE SELECT ON provider_audit_events FROM licensing_runtime");
        await AssertStatusAsync(invitee, Audit, HttpStatusCode.Forbidden);
        await AssertStatusAsync(invitee, Audit + "?scope=merchant-canary&cursor=secret-canary", HttpStatusCode.Forbidden);
        await AssertStatusAsync(owner, Audit + "?scope=merchant-canary", HttpStatusCode.Forbidden);
        using var inviteeWrite = await invitee.SendAsync(HttpMethod.Post, Invitations, new { email = Invitee });
        Assert.Equal(HttpStatusCode.Forbidden, inviteeWrite.StatusCode);
        using var ownerWrite = await owner.SendAsync(HttpMethod.Post, Invitations, new { email = Invitee },
            origin: "https://untrusted.example.test");
        Assert.Equal(HttpStatusCode.Forbidden, ownerWrite.StatusCode);
        await ExecuteAsync(migrator, "GRANT SELECT ON provider_audit_events TO licensing_runtime");

        Assert.Equal(3L, await ScalarAsync(runtime, "SELECT count(*) FROM provider_audit_events WHERE action=102"));
        Assert.Equal(2L, await ScalarAsync(runtime, "SELECT count(*) FROM provider_audit_events WHERE action=103"));
        Assert.Equal(1L, await ScalarAsync(runtime, "SELECT count(*) FROM admin_invitations"));
        using var read = await ReadAsync(owner, Audit + "?action=audit-read-denied");
        Assert.DoesNotContain("canary", read.RootElement.GetRawText(), StringComparison.Ordinal);
        var rows = read.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(3, rows.Select(x => x.GetProperty("correlationId").GetGuid()).Distinct().Count());
        Assert.All(rows, row => Assert.NotEqual(Guid.Empty, row.GetProperty("actorId").GetGuid()));
        await AssertReadAppendRequiredAsync(runtime, migrator, owner);
    }

    private static async Task AssertReadAppendRequiredAsync(NpgsqlConnectionStringBuilder runtime,
        NpgsqlConnectionStringBuilder migrator, P0110CookieBrowser owner)
    {
        await ExecuteAsync(migrator, """
            CREATE FUNCTION refuse_read_fixture() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW.action=101 THEN RAISE EXCEPTION 'exception-message-canary' USING ERRCODE='55000'; END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER refuse_read_fixture BEFORE INSERT ON provider_audit_events
            FOR EACH ROW EXECUTE FUNCTION refuse_read_fixture();
            """);
        using var unavailable = await owner.SendAsync(HttpMethod.Get, Audit);
        Assert.Equal(HttpStatusCode.InternalServerError, unavailable.StatusCode);
        Assert.True(unavailable.Headers.CacheControl?.NoStore);
        var content = await unavailable.Content.ReadAsStringAsync();
        Assert.DoesNotContain("exception-message-canary", content, StringComparison.Ordinal);
        using var problem = JsonDocument.Parse(content);
        Assert.Equal("Internal.Unexpected", problem.RootElement.GetProperty("code").GetString());
        Assert.False(problem.RootElement.TryGetProperty("items", out _));
        Assert.Equal(1L, await ScalarAsync(runtime, "SELECT count(*) FROM provider_audit_events WHERE action=101"));
    }

    private static async Task AssertUnauthenticatedAsync(LicensingApiFactory api, NpgsqlConnectionStringBuilder runtime,
        P0110CookieBrowser owner, P0110CookieBrowser invitee, P0106Tests.TestClock clock)
    {
        using var fixture = new P0110CookieBrowser(RawClient(api));
        fixture.Set(FixtureCookie, FixtureToken);
        await AssertStatusAsync(fixture, Audit, HttpStatusCode.Unauthorized);
        using var merchant = RawClient(api);
        var jwt = P0109Tests.CreateMerchantToken(clock.GetUtcNow());
        merchant.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        merchant.DefaultRequestHeaders.Add("Cookie", "__Host-InteliBill.Admin.Access=" + jwt);
        using var refused = await merchant.GetAsync(new Uri(Audit, UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        await ExecuteAsync(runtime, "UPDATE admin_sessions SET revoked_at = last_seen_at WHERE admin_id <> (SELECT owner_id FROM initial_admin_bootstrap)");
        await AssertStatusAsync(invitee, Audit, HttpStatusCode.Unauthorized);
        clock.Advance(TimeSpan.FromMinutes(15));
        await AssertStatusAsync(owner, Audit, HttpStatusCode.Unauthorized);
        Assert.Equal(5L, await ScalarAsync(runtime, "SELECT count(*) FROM provider_audit_events WHERE action IN (102,103)"));
    }

    [Fact]
    public async Task RuntimeCannotChangeHistoryAndAuditHasNoMutationRoute()
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        await using var api = new LicensingApiFactory(runtime, clock: clock, settings: Settings());
        using var owner = await SignInOwnerAsync(api, clock);
        foreach (var query in new[] { "?pageSize=101", "?pageSize=0", "?action=unknown", "?cursor=!", "?fromUtc=2026-11-14T12:00:00",
                     "?fromUtc=2026-11-14T12:00:00Z&toUtc=2026-11-14T12:00:00Z" })
            await AssertStatusAsync(owner, Audit + query, HttpStatusCode.BadRequest);
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Delete, HttpMethod.Patch, HttpMethod.Put })
        {
            using var response = await owner.SendAsync(method, Audit);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        var migrator = new NpgsqlConnectionStringBuilder(runtime.ConnectionString) { Username = "licensing_migrator" };
        await ExecuteAsync(migrator, "GRANT TRUNCATE ON provider_audit_events TO licensing_runtime");
        foreach (var statement in new[] { "UPDATE provider_audit_events SET action=action", "DELETE FROM provider_audit_events",
                     "TRUNCATE provider_audit_events" })
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(runtime, statement));
            Assert.Equal("55000", denied.SqlState);
        }
    }
}
