using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0107Tests
{
    [Fact]
    public async Task InvitationsRequireScopedCapabilityPersistedOwnerAndRecentMfa()
    {
        var provider = await ProviderAsync();
        var invitee = NewInvitee();
        await AssertRefusedAsync(provider, invitee, HttpStatusCode.Forbidden,
            settings => settings["AdminFixtures:Capabilities"] = "billing.payments.verify");
        await AssertRefusedAsync(provider, invitee, HttpStatusCode.Forbidden,
            settings => settings["AdminFixtures:AdminId"] = Guid.NewGuid().ToString());
        await AssertRefusedAsync(provider, invitee, HttpStatusCode.Forbidden, settings =>
            settings["AdminFixtures:MfaVerifiedAt"] = provider.Clock.GetUtcNow().AddMinutes(-5).ToString("O", CultureInfo.InvariantCulture));
        await AssertRefusedAsync(provider, invitee, HttpStatusCode.Forbidden, settings =>
            settings["AdminFixtures:MfaVerifiedAt"] = provider.Clock.GetUtcNow().AddSeconds(1).ToString("O", CultureInfo.InvariantCulture));

        await using var api = Host(provider);
        using (var anonymous = Browser(api))
            Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(anonymous, InvitationsPath + "/context"));
        using (var forged = Browser(api, provider.Session + "x"))
            Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(forged, InvitationsPath + "/context"));
        using var owner = Browser(api, provider.Session);
        using (var foreign = await InviteAsync(owner, invitee, "https://elsewhere.test"))
            Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        using (var csrf = await InviteAsync(owner, invitee, csrf: "forged"))
            Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode);

        // MFA just under five minutes old is honored; at five minutes it is not.
        var token = await ReadTokenAsync(owner, InvitationsPath + "/context");
        provider.Clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        using (var recent = await InviteAsync(owner, invitee, csrf: token)) Assert.Equal(HttpStatusCode.Created, recent.StatusCode);
        provider.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var stale = await InviteAsync(owner, invitee, csrf: token)) Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(owner, InvitationsPath + "/context"));
        Assert.Single(await mail.ReadAsync(invitee));
    }

    [Fact]
    public async Task PendingEnrollmentAndForeignCredentialsCannotInvite()
    {
        var provider = await ProviderAsync();
        var invitee = NewInvitee();
        await using var api = Host(provider);
        using var owner = Browser(api, provider.Session);
        using (var issued = await InviteAsync(owner, invitee)) Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var token = (await ReadLinkTokensAsync(invitee)).Single();
        using var enrolled = Browser(api);
        using (var accepted = await AcceptAsync(enrolled, token, invitee, NewPassword()))
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        // The MFA-pending invitee holds no invitation authority, by cookie, bearer or header.
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(enrolled, InvitationsPath + "/context"));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(InvitationsPath, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.Session);
        request.Headers.Add("Origin", Origin);
        request.Headers.Add("X-Forwarded-User", provider.OwnerId.ToString());
        request.Headers.Add("X-Admin-Capabilities", Capability);
        request.Content = System.Net.Http.Json.JsonContent.Create(new { email = NewInvitee() });
        using (var anonymous = Browser(api))
        using (var refused = await anonymous.SendAsync(request))
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        // The owner's synthesized MFA is the fixture's only enrolled identity; without it, issuance fails closed.
        var csrf = await ReadTokenAsync(owner, InvitationsPath + "/context");
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Login(provider.Runtime.Database!, LicensingPostgreSqlFixture.MigratorRole),
            "UPDATE admin_identities SET mfa_enrolled_at = NULL");
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(owner, InvitationsPath + "/context"));
        using (var unenrolled = await InviteAsync(owner, NewInvitee(), csrf: csrf))
            Assert.Equal(HttpStatusCode.Forbidden, unenrolled.StatusCode);
        Assert.Equal(1L, await ScalarAsync(provider.Runtime, "SELECT count(*) FROM admin_invitations"));
    }

    [Fact]
    public async Task FixtureSessionsAreRefusedOutsideDevelopmentAndIssuanceFailsClosed()
    {
        var provider = await ProviderAsync();
        await using (var production = Host(provider, environment: "Production"))
        {
            var failure = Record.Exception(() => production.CreateClient());
            Assert.NotNull(failure);
        }

        await using var unconfigured = Host(provider, settings =>
        {
            // Compose passes the unset fixture as empty values.
            foreach (var key in settings.Keys.Where(key => key.StartsWith("AdminFixtures:", StringComparison.Ordinal)).ToArray())
                settings[key] = "";
        }, environment: "Production");
        using var browser = Browser(unconfigured, provider.Session);
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(browser, InvitationsPath + "/context"));
        Assert.Equal(0L, await ScalarAsync(provider.Runtime, "SELECT count(*) FROM admin_invitations"));
    }

    [Fact]
    public async Task UndeliverableInvitationIsRetiredAndReported()
    {
        var provider = await ProviderAsync();
        await using var api = Host(provider, settings => settings["AdminMail:Port"] = "9");
        using var owner = Browser(api, provider.Session);

        using var failed = await InviteAsync(owner, NewInvitee());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(1L, await ScalarAsync(provider.Runtime, "SELECT count(*) FROM admin_invitations WHERE revoked_at IS NOT NULL"));
        Assert.Equal(1L, await ScalarAsync(provider.Runtime, "SELECT count(*) FROM provider_audit_events WHERE action = 11"));
    }

    private async Task AssertRefusedAsync(Provider provider, string invitee, HttpStatusCode expected,
        Action<Dictionary<string, string?>> adjust)
    {
        await using var api = Host(provider, adjust);
        using var owner = Browser(api, provider.Session);
        using var context = await owner.GetAsync(new Uri(InvitationsPath + "/context", UriKind.Relative));
        if (context.StatusCode != HttpStatusCode.OK)
        {
            Assert.Equal(expected, context.StatusCode);
            return;
        }

        using var response = await InviteAsync(owner, invitee);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0L, await ScalarAsync(provider.Runtime, "SELECT count(*) FROM admin_invitations"));
    }
}
