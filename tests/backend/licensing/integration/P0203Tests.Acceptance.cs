using System.Net;
using System.Net.Http.Json;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0203Tests
{
    [Fact]
    public async Task NamedVerifiedActor_AcceptsExactlyOnce()
    {
        await using var host = await StartAsync();
        var owner = await host.SignInAsync();
        var named = await host.SignInAsync();
        var stranger = await host.SignInAsync();
        var account = await CreateAccountAsync(owner.Client);
        var shopAccess = await named.Client.GetStringAsync(Path("/api/shops/me"));
        using var issue = await IssueAsync(owner.Client, account, named.Email);
        Assert.Equal(HttpStatusCode.Accepted, issue.StatusCode);
        var invitation = Assert.Single(await InboxAsync(named.Client)).GetProperty("invitationId").GetGuid();

        using var refused = await AcceptAsync(stranger.Client, invitation);
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        using var accepted = await AcceptAsync(named.Client, invitation);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("Manager", (await JsonAsync(accepted)).GetProperty("role").GetString());
        using var replay = await AcceptAsync(named.Client, invitation);
        Assert.Equal(HttpStatusCode.NotFound, replay.StatusCode);
        Assert.Empty(await InboxAsync(named.Client));
        Assert.Equal(shopAccess, await named.Client.GetStringAsync(Path("/api/shops/me")));

        using var members = await named.Client.GetAsync(Path($"/api/billing/accounts/{account:D}/members"));
        Assert.Equal(HttpStatusCode.OK, members.StatusCode);
        var roster = await JsonAsync(members);
        Assert.Equal(2, roster.GetProperty("members").GetArrayLength());
        Assert.Equal("Manager", Assert.Single(roster.GetProperty("members").EnumerateArray(),
            item => item.GetProperty("isCurrent").GetBoolean()).GetProperty("role").GetString());
        Assert.Equal(2L, await P0202Fixture.CountAsync(database, host.Fixture, "SELECT count(*) FROM billing_invitation_audit"));
    }

    private static Task<HttpResponseMessage> AcceptAsync(HttpClient client, Guid invitation, int revision = 1) =>
        client.PostAsJsonAsync(Path($"/api/billing/invitations/{invitation:D}/accept"),
            new { expectedInvitationRevision = revision });
}
