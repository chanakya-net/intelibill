using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0203Tests
{
    private const string InternalInvitations = "/internal/licensing/v1/customer/invitations";
    private const string ProofAction = "billing.invitation.proof";
    private const string AcceptAction = "billing.invitation.accept";

    [Fact]
    public async Task ExactExpiryAndCrossInvitationProofAreRefused()
    {
        await using var host = await StartAsync();
        var owner = await host.SignInAsync();
        var named = await host.SignInAsync();
        var first = await CreateAccountAsync(owner.Client);
        var second = await CreateAccountAsync(owner.Client);
        await AssertGenericIssueAsync(owner.Client, first, named.Email);
        await AssertGenericIssueAsync(owner.Client, second, named.Email);
        var invitations = await InboxAsync(named.Client);
        Assert.Equal(2, invitations.Length);
        var id = invitations[0].GetProperty("invitationId").GetGuid();
        var other = invitations[1].GetProperty("invitationId").GetGuid();
        var expiry = invitations[0].GetProperty("expiresAt").GetDateTimeOffset();
        using var client = host.Licensing.CreateClient();
        host.Fixture.Clock.Now = expiry.AddTicks(-10);
        using var proof = await SendInvitationAsync(client, host.Fixture, named.Actor, id, "proof", ProofAction, []);
        Assert.Equal(HttpStatusCode.OK, proof.StatusCode);
        var body = AcceptanceBody((await JsonAsync(proof)).GetProperty("token").GetString()!);
        using var wrongPurpose = await SendInvitationAsync(client, host.Fixture, named.Actor, other, "accept", AcceptAction, body);
        Assert.Equal(HttpStatusCode.NotFound, wrongPurpose.StatusCode);

        host.Fixture.Clock.Now = expiry;
        using var expired = await SendInvitationAsync(client, host.Fixture, named.Actor, id, "accept", AcceptAction, body);
        Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode);
        using var expiredProof = await SendInvitationAsync(client, host.Fixture, named.Actor, id, "proof", ProofAction, []);
        Assert.Equal(HttpStatusCode.NotFound, expiredProof.StatusCode);
        Assert.Equal(0L, await P0202Fixture.CountAsync(database, host.Fixture,
            "SELECT count(*) FROM public.billing_account_members WHERE role = 2"));
    }

    [Fact]
    public async Task TwoReplicas_GrantOneManagerAndOneAudit()
    {
        await using var host = await StartAsync();
        var owner = await host.SignInAsync();
        var named = await host.SignInAsync();
        var account = await CreateAccountAsync(owner.Client);
        await AssertGenericIssueAsync(owner.Client, account, named.Email);
        var id = Assert.Single(await InboxAsync(named.Client)).GetProperty("invitationId").GetGuid();

        await using var other = host.Replica();
        using var a = host.Licensing.CreateClient();
        using var b = other.CreateClient();
        using var proof = await SendInvitationAsync(b, host.Fixture, named.Actor, id, "proof", ProofAction, []);
        Assert.Equal(HttpStatusCode.OK, proof.StatusCode);
        var body = AcceptanceBody((await JsonAsync(proof)).GetProperty("token").GetString()!);

        await using var connection = new NpgsqlConnection(host.Fixture.Owner.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var fence = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1, 0))", connection, transaction);
        fence.Parameters.AddWithValue($"b{account:D}");
        await fence.ExecuteNonQueryAsync();
        var one = SendInvitationAsync(a, host.Fixture, named.Actor, id, "accept", AcceptAction, body);
        var two = SendInvitationAsync(b, host.Fixture, named.Actor, id, "accept", AcceptAction, body);
        await WaitForBothAsync(connection, transaction);
        await transaction.CommitAsync();

        using var first = await one;
        using var second = await two;
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.NotFound },
            new[] { first.StatusCode, second.StatusCode }.Order());
        Assert.Equal(1L, await P0202Fixture.CountAsync(database, host.Fixture, """
            SELECT count(*) FROM public.billing_account_invitations
            WHERE revision = 2 AND sealed_proof IS NULL AND accepted_member_id IS NOT NULL AND octet_length(proof_hash) = 32
            """));
        Assert.Equal(1L, await P0202Fixture.CountAsync(database, host.Fixture, """
            SELECT count(*) FROM public.billing_invitation_audit WHERE action = 'Accepted' AND invitation_revision = 2
            """));
        Assert.Equal(1L, await P0202Fixture.CountAsync(database, host.Fixture, """
            SELECT count(*) FROM public.billing_account_members WHERE role = 2 AND status = 1 AND revision = 1
            """));
    }

    private static async Task WaitForBothAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2.5));
        await using var read = new NpgsqlCommand("""
            SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted
              AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
            """, connection, transaction);
        while ((long)(await read.ExecuteScalarAsync(deadline.Token))! < 2)
            await Task.Delay(10, deadline.Token);
    }

    private static byte[] AcceptanceBody(string proof) =>
        JsonSerializer.SerializeToUtf8Bytes(new { token = proof, expectedInvitationRevision = 1 });

    private static Task<HttpResponseMessage> SendInvitationAsync(HttpClient client, P0201Fixture fixture,
        Guid actor, Guid id, string suffix, string action, byte[] bytes)
    {
        var path = $"{InternalInvitations}/{id:D}/{suffix}";
        var signed = P0202Fixture.Sign(fixture, actor, P0202Fixture.Create, bytes, data =>
        {
            data["action"] = action;
            data["path"] = path;
            data["body_sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
        });
        return P0202Fixture.SendAsync(client, signed, path: path);
    }
}
