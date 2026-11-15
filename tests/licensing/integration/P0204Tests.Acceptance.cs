using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0204Tests
{
    [Fact]
    public async Task AccountCreationKeyReplaysWithFormerOwnersCurrentRole()
    {
        await using var host = await StartAsync();
        var owner = await host.SignInAsync();
        var recipient = await host.SignInAsync();
        var request = new { operationKey = Guid.NewGuid(), name = "Billing transfer fixture" };
        var accounts = Path("/api/billing/accounts");
        using var created = await owner.Client.PostAsJsonAsync(accounts, request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdAccount = await JsonAsync(created);
        Assert.Equal("Owner", createdAccount.GetProperty("role").GetString());
        var account = createdAccount.GetProperty("accountId").GetGuid();
        var transfer = await ProposeAsync(owner.Client, account, recipient.Client, recipient.Email);
        using var accepted = await AcceptAsync(recipient.Client, transfer, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        using var replay = await owner.Client.PostAsJsonAsync(accounts, request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayedAccount = await JsonAsync(replay);
        Assert.Equal(account, replayedAccount.GetProperty("accountId").GetGuid());
        Assert.Equal(request.name, replayedAccount.GetProperty("name").GetString());
        Assert.Equal("Manager", replayedAccount.GetProperty("role").GetString());
    }

    [Fact]
    public async Task NamedRecipientAccepts_OneOwnerAndHistoricalResponsibilityRemain()
    {
        await using var host = await StartAsync();
        var owner = await host.SignInAsync();
        var named = await host.SignInAsync();
        var stranger = await host.SignInAsync();
        var account = await CreateAccountAsync(owner.Client);
        var shopAccess = await named.Client.GetStringAsync("/api/shops/me");
        using var issued = await IssueAsync(owner.Client, account, named.Email);
        Assert.Equal(HttpStatusCode.Accepted, issued.StatusCode);
        var transfer = Assert.Single(await InboxAsync(named.Client)).GetProperty("transferId").GetGuid();
        var path = $"/api/billing/ownership-transfers/{transfer:D}";
        var preview = await named.Client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(owner.Actor, preview.GetProperty("ownerActorId").GetGuid());
        Assert.Equal(named.Actor, preview.GetProperty("recipientActorId").GetGuid());
        Assert.Equal(host.Fixture.Clock.Now.AddDays(7), preview.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Empty(await InboxAsync(stranger.Client));

        var key = Guid.NewGuid();
        using var wrongActor = await AcceptAsync(stranger.Client, transfer, key);
        Assert.Equal(HttpStatusCode.NotFound, wrongActor.StatusCode);
        using var withoutConsent = await AcceptAsync(named.Client, transfer, key, confirmed: false);
        Assert.Equal(HttpStatusCode.BadRequest, withoutConsent.StatusCode);
        using var accepted = await AcceptAsync(named.Client, transfer, key);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("Owner", (await JsonAsync(accepted)).GetProperty("role").GetString());
        using var replay = await AcceptAsync(named.Client, transfer, key);
        Assert.Equal(await accepted.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Empty(await InboxAsync(named.Client));
        Assert.Equal(shopAccess, await named.Client.GetStringAsync("/api/shops/me"));

        var roster = await owner.Client.GetFromJsonAsync<JsonElement>($"/api/billing/accounts/{account:D}/members");
        Assert.Equal(2, roster.GetProperty("members").GetArrayLength());
        Assert.Equal("Manager", Assert.Single(roster.GetProperty("members").EnumerateArray(),
            member => member.GetProperty("isCurrent").GetBoolean()).GetProperty("role").GetString());
        Assert.Equal(1L, await P0202Fixture.CountAsync(database, host.Fixture,
            $"SELECT count(*) FROM billing_accounts WHERE id = '{account:D}' AND created_by_merchant_user_id = '{owner.Actor:D}'"));
    }

    private static Task<HttpResponseMessage> AcceptAsync(HttpClient caller, Guid transfer, Guid key,
        bool confirmed = true, int version = 1) =>
        caller.PostAsJsonAsync($"/api/billing/ownership-transfers/{transfer:D}/accept",
            new { operationKey = key, expectedVersion = version, responsibilityPolicyVersion = "prospective-billing-owner/1",
                acceptResponsibility = confirmed });

    private static async Task<JsonElement[]> InboxAsync(HttpClient caller)
    {
        var inbox = await caller.GetFromJsonAsync<JsonElement>("/api/billing/ownership-transfers");
        return [.. inbox.GetProperty("transfers").EnumerateArray()];
    }
}
