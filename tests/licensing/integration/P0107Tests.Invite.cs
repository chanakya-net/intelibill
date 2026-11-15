using System.Net;
using System.Text.Json;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0107Tests
{
    [Fact]
    public async Task InvitedAdmin_AcceptsOnceAndOnlyEntersMfaEnrollment()
    {
        var provider = await ProviderAsync();
        var logs = new CapturedLogs();
        await using var api = Host(provider, logs: logs);
        using var owner = Browser(api, provider.Session);
        var invitee = NewInvitee();

        using (var issued = await InviteAsync(owner, invitee))
        {
            Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
            Assert.True(issued.Headers.CacheControl?.NoStore);
            var body = await issued.Content.ReadAsStringAsync();
            Assert.DoesNotContain("i1_", body, StringComparison.Ordinal);
            using var json = JsonDocument.Parse(body);
            Assert.Equal(provider.Clock.GetUtcNow().AddHours(72), json.RootElement.GetProperty("expiresAt").GetDateTimeOffset());
        }

        var token = (await ReadLinkTokensAsync(invitee)).Single();
        await AssertOnlyDigestStoredAsync(provider, token);

        await using var restarted = Host(provider, logs: logs);
        using var fresh = Browser(restarted);
        var password = NewPassword();
        using (var accepted = await AcceptAsync(fresh, token, invitee, password))
        {
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            using var json = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
            Assert.Equal("enrollmentRequired", json.RootElement.GetProperty("state").GetString());
            var cookie = EnrollmentCookieOf(accepted);
            Assert.NotNull(cookie);
            Assert.Contains("httponly", cookie.Value.Attributes, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=strict", cookie.Value.Attributes, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(fresh, EnrollmentPath));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(fresh, BusinessPath));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(fresh, InvitationsPath + "/context"));

        using var replay = await AcceptAsync(Browser(restarted), token, invitee, NewPassword());
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Null(EnrollmentCookieOf(replay));

        await AssertEnrolledOnceAsync(provider, invitee);
        // The async Serilog sink delivers after the response; wait for it instead of racing it.
        await logs.WaitForAsync("Admin invitation accepted");
        await logs.WaitForAsync("Invitation.Unavailable");
        Assert.DoesNotContain(logs.Events, entry => entry.Contains(token, StringComparison.Ordinal)
            || entry.Contains(token[3..], StringComparison.Ordinal) || entry.Contains(password, StringComparison.Ordinal)
            || entry.Contains(invitee, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReissuingAnInvitation_RetiresThePreviousLink()
    {
        var provider = await ProviderAsync();
        await using var api = Host(provider);
        using var owner = Browser(api, provider.Session);
        var invitee = NewInvitee();
        using (var first = await InviteAsync(owner, invitee)) Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using (var second = await InviteAsync(owner, invitee)) Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var tokens = await ReadLinkTokensAsync(invitee, 2);

        using var retired = await AcceptAsync(Browser(api), tokens[1], invitee, NewPassword());
        Assert.Equal(HttpStatusCode.Conflict, retired.StatusCode);
        using var current = await AcceptAsync(Browser(api), tokens[0], invitee, NewPassword());
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        await AssertEnrolledOnceAsync(provider, invitee);

        using var existing = await InviteAsync(owner, invitee);
        Assert.Equal(HttpStatusCode.Conflict, existing.StatusCode);
        using var ownerAddress = await InviteAsync(owner, "fixture.owner@example.test");
        Assert.Equal(HttpStatusCode.Conflict, ownerAddress.StatusCode);
    }

    private static async Task AssertOnlyDigestStoredAsync(Provider provider, string token)
    {
        Assert.Equal(1L, await ScalarAsync(provider.Runtime, $"""
            SELECT count(*) FROM admin_invitations
            WHERE token_digest = '{Digest(token)}' AND accepted_at IS NULL AND revoked_at IS NULL
                AND invited_by = '{provider.OwnerId}'
            """));
        var stored = (string)(await ScalarAsync(provider.Runtime, """
            SELECT coalesce(string_agg(row_to_json(i)::text, ''), '') FROM admin_invitations i
            """))!;
        Assert.DoesNotContain(token[3..], stored, StringComparison.Ordinal);
    }

    private static async Task AssertEnrolledOnceAsync(Provider provider, string invitee)
    {
        Assert.Equal(1L, await ScalarAsync(provider.Runtime, $"""
            SELECT count(*) FROM admin_identities a JOIN admin_invitations i ON i.accepted_admin_id = a.id
            WHERE a.email = upper('{invitee}') AND a.mfa_enrolled_at IS NULL AND length(a.password_hash) = 60
                AND a.verification_reference = 'invitation:' || i.id AND i.accepted_at = a.verified_at
            """));
        Assert.Equal(1L, await ScalarAsync(provider.Runtime, $"""
            SELECT count(*) FROM provider_audit_events e JOIN admin_identities a ON a.id = e.owner_id
            WHERE e.action = 8 AND a.email = upper('{invitee}') AND e.invitation_id IS NOT NULL
            """));
        Assert.True((long)(await ScalarAsync(provider.Runtime, $"""
            SELECT count(*) FROM provider_audit_events WHERE action = 7 AND owner_id = '{provider.OwnerId}'
            """))! >= 1);
    }
}
