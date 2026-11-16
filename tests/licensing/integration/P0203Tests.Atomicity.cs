using System.Net;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0203Tests
{
    [Fact]
    public async Task FailedAuditRollsBackAcceptanceAndManagerGrant()
    {
        await using var host = await StartAsync();
        var owner = await host.SignInAsync();
        var named = await host.SignInAsync();
        var account = await CreateAccountAsync(owner.Client);
        await AssertGenericIssueAsync(owner.Client, account, named.Email);
        var invitation = Assert.Single(await InboxAsync(named.Client)).GetProperty("invitationId").GetGuid();
        await LicensingPostgreSqlFixture.ExecuteAsync(host.Fixture.Owner,
            "REVOKE INSERT ON public.billing_invitation_audit FROM licensing_runtime");

        using var rejected = await AcceptAsync(named.Client, invitation);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
        Assert.Equal(1, Assert.Single(await InboxAsync(named.Client)).GetProperty("revision").GetInt32());
        Assert.Equal(0L, await P0202Fixture.CountAsync(database, host.Fixture,
            "SELECT count(*) FROM public.billing_account_members WHERE role = 2"));

        await LicensingPostgreSqlFixture.ExecuteAsync(host.Fixture.Owner,
            "GRANT INSERT ON public.billing_invitation_audit TO licensing_runtime");
        using var accepted = await AcceptAsync(named.Client, invitation);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Empty(await InboxAsync(named.Client));
    }

    [Fact]
    public async Task StaleIssuingOwnerRevisionCannotBeAccepted()
    {
        await using var host = await StartAsync();
        var owner = await host.SignInAsync();
        var named = await host.SignInAsync();
        var account = await CreateAccountAsync(owner.Client);
        await AssertGenericIssueAsync(owner.Client, account, named.Email);
        var invitation = Assert.Single(await InboxAsync(named.Client)).GetProperty("invitationId").GetGuid();

        await using var connection = new NpgsqlConnection(database.Bootstrap(host.Fixture.Owner.Database).ConnectionString);
        await connection.OpenAsync();
        await using var revision = new NpgsqlCommand("""
            UPDATE public.billing_account_members SET revision = 2
            WHERE account_id = $1 AND merchant_user_id = $2
            """, connection);
        revision.Parameters.AddWithValue(account);
        revision.Parameters.AddWithValue(owner.Actor);
        Assert.Equal(1, await revision.ExecuteNonQueryAsync());

        using var rejected = await AcceptAsync(named.Client, invitation);
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        Assert.Equal(0L, await P0202Fixture.CountAsync(database, host.Fixture,
            "SELECT count(*) FROM public.billing_account_members WHERE role = 2"));
    }
}
