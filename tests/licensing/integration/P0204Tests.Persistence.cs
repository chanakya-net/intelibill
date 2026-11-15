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
    public async Task ConcurrentTransfers_AcrossReplicasCommitExactlyOneOwnerChange(bool sameProposal)
    {
        await using var host = await StartAsync();
        var owner = await host.SignInAsync();
        var first = await host.SignInAsync();
        var second = sameProposal ? first : await host.SignInAsync();
        var account = await CreateAccountAsync(owner.Client);
        var a = await ProposeAsync(owner.Client, account, first.Client, first.Email);
        var b = sameProposal ? a : await ProposeAsync(owner.Client, account, second.Client, second.Email);
        var firstKey = Guid.NewGuid();
        var secondKey = sameProposal ? firstKey : Guid.NewGuid();

        await using var connection = new NpgsqlConnection(database.Bootstrap(host.Fixture.Owner.Database).ConnectionString);
        await connection.OpenAsync();
        await using var fence = await P0203bTests.HoldAccountFenceAsync(connection, account);
        await using var replica = host.Replica();
        using var clientA = host.Licensing.CreateClient();
        using var clientB = replica.CreateClient();
        var resultA = SendAcceptanceAsync(clientA, host, first.Actor, a, firstKey);
        var resultB = SendAcceptanceAsync(clientB, host, second.Actor, b, secondKey);
        await P0203bTests.WaitForLockAsync(connection, fence, "advisory", 2);
        Assert.Equal(1L, await CountAsync(host, "SELECT count(*) FROM billing_account_members WHERE role = 1 AND status = 1"));
        await fence.CommitAsync();

        using var answerA = await resultA;
        using var answerB = await resultB;
        var statuses = new[] { answerA.StatusCode, answerB.StatusCode }.OrderBy(x => x).ToArray();
        Assert.Equal(new[] { HttpStatusCode.OK, sameProposal ? HttpStatusCode.OK : HttpStatusCode.Conflict }, statuses);
        Assert.Equal(1L, await CountAsync(host, "SELECT count(*) FROM billing_account_members WHERE role = 1 AND status = 1"));
        Assert.Equal(1L, await CountAsync(host, "SELECT count(*) FROM billing_ownership_acceptances"));
    }

    [Fact]
    public async Task FailedEvidenceWrite_RollsBackTransferAndDelegationUse()
    {
        await using var host = await StartAsync();
        var owner = await host.SignInAsync();
        var recipient = await host.SignInAsync();
        var account = await CreateAccountAsync(owner.Client);
        var proposal = await ProposeAsync(owner.Client, account, recipient.Client, recipient.Email);
        var key = Guid.NewGuid();
        var bootstrap = database.Bootstrap(host.Fixture.Owner.Database);
        await LicensingPostgreSqlFixture.ExecuteAsync(bootstrap,
            "ALTER TABLE billing_ownership_acceptances ADD CONSTRAINT fixture_failure CHECK (false)");
        using var failed = await AcceptAsync(recipient.Client, proposal, key);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(1L, await CountAsync(host, $"SELECT count(*) FROM billing_account_members WHERE role = 1 AND revision = 1 AND merchant_user_id = '{owner.Actor:D}'"));
        Assert.Equal(0L, await CountAsync(host, "SELECT count(*) FROM billing_ownership_acceptances"));
        Assert.Equal(0L, await CountAsync(host, "SELECT count(*) FROM merchant_billing_delegation_uses WHERE action = 'billing.ownership-transfer.accept'"));
        Assert.Single(await InboxAsync(recipient.Client));

        await LicensingPostgreSqlFixture.ExecuteAsync(bootstrap,
            "ALTER TABLE billing_ownership_acceptances DROP CONSTRAINT fixture_failure");
        using var committed = await AcceptAsync(recipient.Client, proposal, key);
        Assert.Equal(HttpStatusCode.OK, committed.StatusCode);
        var removal = await Assert.ThrowsAsync<PostgresException>(() => LicensingPostgreSqlFixture.ExecuteAsync(bootstrap,
            "UPDATE billing_ownership_acceptances SET effective_at = now()"));
        Assert.Equal("55000", removal.SqlState);
        var zeroOwners = await Assert.ThrowsAsync<PostgresException>(() => LicensingPostgreSqlFixture.ExecuteAsync(bootstrap,
            "UPDATE billing_account_members SET role = 2 WHERE role = 1"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, zeroOwners.SqlState);
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(host.Fixture.Runtime,
            "SELECT count(*) FROM billing_ownership_acceptances"));
    }

    [Fact]
    public async Task ProposalExpiryWhileWaitingOnFencePreventsAcceptance()
    {
        await using var host = await StartAsync();
        var owner = await host.SignInAsync();
        var named = await host.SignInAsync();
        var account = await CreateAccountAsync(owner.Client);
        var transfer = await ProposeAsync(owner.Client, account, named.Client, named.Email);
        var deadline = host.Fixture.Clock.Now.AddDays(7);
        host.Fixture.Clock.Now = deadline.AddSeconds(-1);
        await using var connection = new NpgsqlConnection(database.Bootstrap(host.Fixture.Owner.Database).ConnectionString);
        await connection.OpenAsync();
        await using var fence = await P0203bTests.HoldAccountFenceAsync(connection, account);
        using var client = host.Licensing.CreateClient();
        var acceptance = SendAcceptanceAsync(client, host, named.Actor, transfer, Guid.NewGuid());
        await P0203bTests.WaitForLockAsync(connection, fence, "advisory");
        host.Fixture.Clock.Now = deadline;
        await fence.CommitAsync();
        using var refused = await acceptance;
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(0L, await CountAsync(host, "SELECT count(*) FROM billing_ownership_acceptances"));
    }

    private Task<long> CountAsync(TransferHost host, string sql) => P0202Fixture.CountAsync(database, host.Fixture, sql);

    private static async Task<Guid> ProposeAsync(HttpClient owner, Guid account, HttpClient recipient, string email, int version = 1)
    {
        var prior = (await InboxAsync(recipient)).Select(row => row.GetProperty("transferId").GetGuid()).ToHashSet();
        using var response = await IssueAsync(owner, account, email, version);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return Assert.Single((await InboxAsync(recipient)).Select(row => row.GetProperty("transferId").GetGuid()),
            id => !prior.Contains(id));
    }

    private static Task<HttpResponseMessage> SendAcceptanceAsync(HttpClient client, TransferHost host, Guid actor,
        Guid transfer, Guid key)
    {
        var path = BillingOwnershipTransfersV1.AcceptPath(transfer);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new AcceptBillingOwnershipTransferV1(key, 1,
            BillingOwnershipTransfersV1.Policy, true));
        return P0202Fixture.SendAsync(client, P0202Fixture.Sign(host.Fixture, actor, P0202Fixture.Create, bytes, claims =>
        {
            claims["action"] = BillingOwnershipTransfersV1.Accept;
            claims["path"] = path;
        }), path: path);
    }
}
