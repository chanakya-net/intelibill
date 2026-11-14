using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Licensing.Application.AdminIdentities;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0114Tests
{
    private const string Invitations = "/api/admin/invitations";
    private const string Acceptance = "/api/admin/auth/invitation";
    private const string Enrollment = "/api/admin/auth/bootstrap/enrollment/mfa";
    private const string Invitee = "merchant-record-canary@example.test";
    private const string InviteePassword = "Fixture--Password-Canary-844";

    [Fact]
    public async Task InvitationTransitionSurvivesRestartAndRollsBackWithoutAudit()
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        var settings = Settings();
        var mailer = new FixtureMailer();
        var api = new LicensingApiFactory(runtime, clock: clock, settings: settings,
            configure: services => services.AddSingleton<IAdminInvitationMailer>(mailer));
        Guid invitation;
        long before;
        string token;
        using var owner = await SignInOwnerAsync(api, clock);
        await using (api)
        {
            invitation = await IssueAsync(owner, Invitee);
            token = mailer.Token;
            before = await VersionAsync(runtime, invitation);
            await AssertFailedAppendAsync(api, runtime, token, invitation, before);
            using var invitee = EnrollmentClient(api);
            using var accepted = await AcceptAsync(invitee, token);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        await using var restarted = new LicensingApiFactory(runtime, clock: clock, settings: settings);
        using var resumed = owner.Fork(RawClient(restarted));
        using var history = await ReadAsync(resumed, Audit + "?action=invitation-accepted");
        var row = Assert.Single(history.RootElement.GetProperty("items").EnumerateArray());
        CheckReference(row.GetProperty("before"), invitation, "Open", before);
        CheckReference(row.GetProperty("after"), invitation, "Accepted", await VersionAsync(runtime, invitation));
        Assert.Equal("InvitationAccepted", row.GetProperty("reasonCode").GetString());
        Assert.NotEqual(Guid.Empty, row.GetProperty("correlationId").GetGuid());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("actorId").ValueKind);
        using var complete = await ReadAsync(resumed, Audit);
        AssertNoPrivateData(complete.RootElement.GetRawText(), token);
        var stored = (string)(await ScalarAsync(runtime, "SELECT string_agg(to_jsonb(e)::text, '') FROM provider_audit_events e"))!;
        AssertNoPrivateData(stored, token);
    }

    private static async Task AssertFailedAppendAsync(LicensingApiFactory api, NpgsqlConnectionStringBuilder runtime,
        string token, Guid invitation, long before)
    {
        var dbOwner = new NpgsqlConnectionStringBuilder(runtime.ConnectionString)
        {
            Username = LicensingPostgreSqlFixture.MigratorRole,
        };
        await ExecuteAsync(dbOwner, """
            ALTER TABLE provider_audit_events ADD CONSTRAINT p0114_refuse_acceptance
            CHECK (action <> 8 AND correlation_id::text IS DISTINCT FROM 'exception-message-canary');
            """);
        using var browser = EnrollmentClient(api);
        using var rejected = await AcceptAsync(browser, token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
        AssertNoPrivateData(await rejected.Content.ReadAsStringAsync(), token);
        Assert.Equal(before, await VersionAsync(runtime, invitation));
        Assert.Equal(1L, await ScalarAsync(runtime, "SELECT count(*) FROM admin_identities"));
        Assert.Equal(0L, await ScalarAsync(runtime, "SELECT count(*) FROM admin_invitations WHERE accepted_at IS NOT NULL"));
        await ExecuteAsync(dbOwner, "ALTER TABLE provider_audit_events DROP CONSTRAINT p0114_refuse_acceptance");
    }

    private static void CheckReference(JsonElement reference, Guid id, string state, long version)
    {
        Assert.Equal("Invitation", reference.GetProperty("targetKind").GetString());
        Assert.Equal(id, reference.GetProperty("targetId").GetGuid());
        Assert.Equal(state, reference.GetProperty("state").GetString());
        Assert.Equal(version, reference.GetProperty("version").GetInt64());
    }

    private static void AssertNoPrivateData(string payload, string token)
    {
        foreach (var canary in new[] { Invitee, InviteePassword, token, "exception-message-canary", "P01-08-fixture" })
            Assert.DoesNotContain(canary, payload, StringComparison.OrdinalIgnoreCase);
        foreach (var name in new[] { "email", "password", "digest", "secret", "sessionId", "merchantId", "shopId", "stackTrace" })
            Assert.DoesNotContain("\"" + name + "\"", payload, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<long> VersionAsync(NpgsqlConnectionStringBuilder runtime, Guid id) =>
        (long)(await ScalarAsync(runtime, $"SELECT xmin::text::bigint FROM admin_invitations WHERE id = '{id}'"))!;

    private static async Task<Guid> IssueAsync(P0110CookieBrowser owner, string email)
    {
        using var context = await ReadAsync(owner, Invitations + "/context");
        using var issued = await owner.SendAsync(HttpMethod.Post, Invitations, new { email },
            context.RootElement.GetProperty("requestToken").GetString(), csrfHeader: "X-Bootstrap-CSRF");
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        using var body = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("invitationId").GetGuid();
    }

    private static HttpClient EnrollmentClient(LicensingApiFactory api) =>
        api.CreateClient(new() { BaseAddress = new Uri(P0110CookieBrowser.Origin) });

    private static Task<HttpResponseMessage> AcceptAsync(HttpClient client, string invitationToken) =>
        PostEnrollmentAsync(client, Acceptance + "/context", Acceptance + "/accept",
            new { invitationToken, email = Invitee, password = InviteePassword });

    private static async Task<HttpResponseMessage> PostEnrollmentAsync(HttpClient client, string context, string path, object body)
    {
        using var source = await client.GetFromJsonAsync<JsonDocument>(new Uri(context, UriKind.Relative));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", P0110CookieBrowser.Origin);
        request.Headers.Add("X-Bootstrap-CSRF", source!.RootElement.GetProperty("requestToken").GetString());
        return await client.SendAsync(request);
    }

    private sealed class FixtureMailer : IAdminInvitationMailer
    {
        internal string Token { get; private set; } = "";

        public Task<bool> TrySendAsync(AdminInvitationMessage message, CancellationToken cancellationToken)
        {
            Token = message.Link.Fragment["#token=".Length..];
            return Task.FromResult(true);
        }
    }
}
