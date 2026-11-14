using System.Net;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0107Tests
{
    [Fact]
    public async Task WrongIdentity_CannotAcceptAndRepeatedMismatchRetiresTheLink()
    {
        var provider = await ProviderAsync();
        await using var api = Host(provider);
        using var owner = Browser(api, provider.Session);
        var invitee = NewInvitee();
        using (var issued = await InviteAsync(owner, invitee)) Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var token = (await ReadLinkTokensAsync(invitee)).Single();
        using var stranger = Browser(api);

        using (var forged = await AcceptAsync(stranger, "i1_" + new string('A', 86), invitee, NewPassword()))
            Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        using (var malformed = await AcceptAsync(stranger, token[..^1], invitee, NewPassword()))
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        using (var weak = await AcceptAsync(stranger, token, invitee, "short"))
            Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var wrong = await AcceptAsync(stranger, token, NewInvitee(), NewPassword());
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            Assert.Null(EnrollmentCookieOf(wrong));
        }

        using var locked = await AcceptAsync(Browser(api), token, invitee, NewPassword());
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        Assert.Equal(0L, await ScalarAsync(provider.Runtime, "SELECT count(*) FROM admin_identities WHERE mfa_enrolled_at IS NULL"));
        Assert.Equal(0L, await ScalarAsync(provider.Runtime, $"SELECT count(*) FROM bootstrap_enrollment_credentials WHERE owner_id <> '{provider.OwnerId}'"));
        var settled = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            LicensingPostgreSqlFixture.ExecuteAsync(provider.Runtime, "UPDATE admin_invitations SET revoked_at = NULL, failed_attempts = 0"));
        Assert.Equal("55000", settled.SqlState);
        Assert.Equal(1L, await ScalarAsync(provider.Runtime, """
            SELECT count(*) FROM admin_invitations WHERE failed_attempts = 5 AND revoked_at IS NOT NULL AND accepted_at IS NULL
            """));
        Assert.Equal(5L, await ScalarAsync(provider.Runtime, "SELECT count(*) FROM provider_audit_events WHERE action = 9"));
    }

    [Theory]
    [InlineData(-1, HttpStatusCode.OK, 1)]
    [InlineData(0, HttpStatusCode.Conflict, 0)]
    [InlineData(1, HttpStatusCode.Conflict, 0)]
    public async Task InvitationExpiresAtTheExactBoundary(int offsetSeconds, HttpStatusCode expected, int admins)
    {
        var provider = await ProviderAsync();
        await using var api = Host(provider);
        using var owner = Browser(api, provider.Session);
        var invitee = NewInvitee();
        using (var issued = await InviteAsync(owner, invitee)) Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var token = (await ReadLinkTokensAsync(invitee)).Single();

        provider.Clock.Advance(TimeSpan.FromHours(72) + TimeSpan.FromSeconds(offsetSeconds));
        using var response = await AcceptAsync(Browser(api), token, invitee, NewPassword());

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal((long)admins, await ScalarAsync(provider.Runtime, "SELECT count(*) FROM admin_identities WHERE mfa_enrolled_at IS NULL"));
    }

    [Fact]
    public async Task ConcurrentAcceptance_EnrollsExactlyOneAdmin()
    {
        var provider = await ProviderAsync();
        await using var firstHost = Host(provider);
        await using var secondHost = Host(provider);
        using var owner = Browser(firstHost, provider.Session);
        var invitee = NewInvitee();
        using (var issued = await InviteAsync(owner, invitee)) Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var token = (await ReadLinkTokensAsync(invitee)).Single();

        using var first = Browser(firstHost);
        using var second = Browser(secondHost);
        var responses = await Task.WhenAll(AcceptAsync(first, token, invitee, NewPassword()),
            AcceptAsync(second, token, invitee, NewPassword()));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Single(responses, response => EnrollmentCookieOf(response) is not null);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }

        await AssertEnrolledOnceAsync(provider, invitee);
        Assert.Equal(1L, await ScalarAsync(provider.Runtime, "SELECT count(*) FROM bootstrap_enrollment_credentials WHERE revoked_at IS NULL AND owner_id <> (SELECT owner_id FROM initial_admin_bootstrap)"));
    }

    [Fact]
    public async Task AcceptanceRequiresTheProviderOriginAndRequestToken()
    {
        var provider = await ProviderAsync();
        await using var api = Host(provider);
        using var owner = Browser(api, provider.Session);
        var invitee = NewInvitee();
        using (var issued = await InviteAsync(owner, invitee)) Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var token = (await ReadLinkTokensAsync(invitee)).Single();
        using var browser = Browser(api);
        var csrf = await ReadTokenAsync(browser, AcceptPath + "/context");
        var body = new { invitationToken = token, email = invitee, password = NewPassword() };

        using (var foreign = await PostAsync(browser, AcceptPath + "/accept", csrf, body, "https://elsewhere.test"))
            Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        using (var forged = await PostAsync(browser, AcceptPath + "/accept", "forged", body))
            Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
        Assert.Equal(0L, await ScalarAsync(provider.Runtime, "SELECT count(*) FROM admin_invitations WHERE accepted_at IS NOT NULL OR failed_attempts > 0"));

        using var accepted = await PostAsync(browser, AcceptPath + "/accept", csrf, body);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }
}
