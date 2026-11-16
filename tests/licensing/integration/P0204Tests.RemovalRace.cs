using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0204Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovalAndAcceptanceRespectTheFirstAuthorityChange(bool acceptFirst)
    {
        await using var host = await StartAsync();
        var owner = await host.SignInAsync();
        var recipient = await host.SignInAsync();
        var account = await CreateAccountAsync(owner.Client);
        var manager = await InviteManagerAsync(owner.Client, account, recipient.Client, recipient.Email);
        var transfer = await ProposeAsync(owner.Client, account, recipient.Client, recipient.Email);

        await using var connection = new NpgsqlConnection(database.Bootstrap(host.Fixture.Owner.Database).ConnectionString);
        await connection.OpenAsync();
        await using var fence = await P0203bTests.HoldAccountFenceAsync(connection, account);
        await using var replica = host.Replica();
        using var recipientApi = replica.CreateClient();
        using var ownerApi = host.Licensing.CreateClient();
        Task<HttpResponseMessage> Accept() => SendAcceptanceAsync(recipientApi, host, recipient.Actor, transfer, Guid.NewGuid());
        Task<HttpResponseMessage> Remove() => RemoveManagerAsync(host, ownerApi, owner.Actor, account, manager);
        var first = acceptFirst ? Accept() : Remove();
        await P0203bTests.WaitForLockAsync(connection, fence, "advisory");
        var second = acceptFirst ? Remove() : Accept();
        await P0203bTests.WaitForLockAsync(connection, fence, "advisory", 2);
        await fence.CommitAsync();

        using var winner = await first;
        using var loser = await second;
        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
        Assert.Equal(acceptFirst ? HttpStatusCode.Forbidden : HttpStatusCode.Conflict, loser.StatusCode);
        var rows = await P0202Fixture.ReadAccountRowsAsync(database, host.Fixture);
        var finalOwner = Assert.Single(rows, row => row.Role == 1 && row.Status == 1);
        Assert.Equal(acceptFirst ? recipient.Actor : owner.Actor, finalOwner.MerchantUserId);
        Assert.Equal(acceptFirst ? 1L : 0L, await CountAsync(host, "SELECT count(*) FROM billing_ownership_acceptances"));
    }

    internal static async Task<Guid> InviteManagerAsync(HttpClient owner, Guid account, HttpClient recipient, string email)
    {
        var path = $"/api/billing/accounts/{account:D}";
        using var issued = await owner.PostAsJsonAsync(path + "/invitations",
            new { operationKey = Guid.NewGuid(), expectedOwnerRevision = 1, recipientKind = "email", recipient = email });
        Assert.Equal(HttpStatusCode.Accepted, issued.StatusCode);
        var inbox = await recipient.GetFromJsonAsync<JsonElement>("/api/billing/invitations");
        var invitation = Assert.Single(inbox.GetProperty("invitations").EnumerateArray()).GetProperty("invitationId").GetGuid();
        using var accepted = await recipient.PostAsJsonAsync($"/api/billing/invitations/{invitation:D}/accept",
            new { expectedInvitationRevision = 1 });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var roster = await owner.GetFromJsonAsync<JsonElement>(path + "/members");
        return Assert.Single(roster.GetProperty("members").EnumerateArray(), row => row.GetProperty("role").GetString() == "Manager")
            .GetProperty("memberId").GetGuid();
    }

    private static Task<HttpResponseMessage> RemoveManagerAsync(TransferHost host, HttpClient client, Guid owner,
        Guid account, Guid manager)
    {
        var path = BillingInvitationsV1.MemberRemoval(account, manager);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new RemoveBillingManagerV1(Guid.NewGuid(), 1, "Fixture removal"));
        return P0202Fixture.SendAsync(client, P0202Fixture.Sign(host.Fixture, owner, P0202Fixture.Create, payload, claims =>
        {
            claims["action"] = BillingInvitationsV1.RemoveMember;
            claims["account_id"] = account;
            claims["path"] = path;
        }), path: path);
    }
}
