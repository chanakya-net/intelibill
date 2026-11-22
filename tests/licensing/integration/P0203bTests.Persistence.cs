using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0203bTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemovalAndAffiliationRequest_SerializeWithoutDeadlock(bool affiliationFirst)
    {
        await using var scenario = await StartAsync();
        await using var db = new NpgsqlConnection(database.Bootstrap(scenario.Fixture.Owner.Database).ConnectionString);
        await db.OpenAsync();
        await using var fence = await HoldAccountFenceAsync(db, scenario.Account);
        await using var replica = P0202Fixture.LicensingApi(scenario.Fixture);
        using var owner = scenario.Licensing.CreateClient();
        using var manager = replica.CreateClient();
        var affiliationPath = ShopAffiliationsV1.AccountPath(scenario.Account);
        var removalPath = BillingInvitationsV1.MemberRemoval(scenario.Account, scenario.Member);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new RequestShopAffiliationV1(Guid.NewGuid(), scenario.Shop, null, true));
        Task<HttpResponseMessage> RequestAsync() => P0202Fixture.SendAsync(manager,
            P0202Fixture.Sign(scenario.Fixture, scenario.ManagerActor, P0202Fixture.Create, payload, claims =>
            {
                claims["action"] = ShopAffiliationsV1.Request;
                claims["account_id"] = scenario.Account;
                claims["path"] = affiliationPath;
            }), path: affiliationPath);
        Task<HttpResponseMessage> RemoveAsync() => P0202Fixture.SendAsync(owner, SignRemoval(scenario, removalPath), path: removalPath);

        var first = affiliationFirst ? RequestAsync() : RemoveAsync();
        await WaitForLockAsync(db, fence, "advisory");
        var second = affiliationFirst ? RemoveAsync() : RequestAsync();
        await WaitForLockAsync(db, fence, "advisory", 2);
        await fence.CommitAsync();

        using var affiliation = await (affiliationFirst ? first : second);
        using var removal = await (affiliationFirst ? second : first);
        Assert.Equal(HttpStatusCode.OK, removal.StatusCode);
        Assert.Equal(affiliationFirst ? HttpStatusCode.Created : HttpStatusCode.Forbidden, affiliation.StatusCode);
        await AssertDeniedAsync(await RequestAsync());
        Assert.Equal(affiliationFirst ? 1L : 0L, await scenario.CountAsync("SELECT count(*) FROM shop_affiliation_requests"));
        Assert.Equal(1L, await scenario.CountAsync("SELECT count(*) FROM billing_member_removals"));
    }

    [Fact]
    public async Task ExpiryWhileWaitingForTheAccountFenceLeavesNoRemoval()
    {
        await using var scenario = await StartAsync();
        await using var db = new NpgsqlConnection(database.Bootstrap(scenario.Fixture.Owner.Database).ConnectionString);
        await db.OpenAsync();
        await using var fence = await HoldAccountFenceAsync(db, scenario.Account);
        using var client = scenario.Licensing.CreateClient();
        var path = BillingInvitationsV1.MemberRemoval(scenario.Account, scenario.Member);
        var proof = SignRemoval(scenario, path);
        var attempt = P0202Fixture.SendAsync(client, proof, path: path);
        await WaitForLockAsync(db, fence, "advisory");
        scenario.Fixture.Clock.Now = scenario.Fixture.Clock.Now.AddSeconds(MerchantBillingDelegationV1.LifetimeSeconds);
        await fence.CommitAsync();
        await AssertCodeAsync(await attempt, HttpStatusCode.Unauthorized, "Billing.DelegationInvalid");
        Assert.Equal(0L, await scenario.CountAsync("SELECT count(*) FROM billing_member_removals"));
        Assert.Equal(0L, await scenario.CountAsync($"SELECT count(*) FROM merchant_billing_delegation_uses WHERE request_id = '{proof.Request:D}'"));
    }

    [Fact]
    public async Task SignedBindingAndLiveAuthorityAreRequiredBeforeReplay()
    {
        await using var scenario = await StartAsync();
        using var client = scenario.Licensing.CreateClient();
        var path = BillingInvitationsV1.MemberRemoval(scenario.Account, scenario.Member);
        var proof = SignRemoval(scenario, path);
        var wrongMember = BillingInvitationsV1.MemberRemoval(scenario.Account, Guid.NewGuid());
        var read = P0201Tests.SignReadAssertion(scenario.Fixture, scenario.Account, scenario.ManagerActor, out var requestId);
        var delayedManagerRead = new P0202Fixture.ActorAssertion(scenario.ManagerActor, requestId, read, [], HttpMethod.Get);
        await AssertCodeAsync(await P0202Fixture.SendAsync(client, proof, path: wrongMember), HttpStatusCode.Unauthorized, "Billing.DelegationInvalid");
        await AssertCodeAsync(await P0202Fixture.SendAsync(client, proof, body: [123, 125], path: path),
            HttpStatusCode.Unauthorized, "Billing.DelegationInvalid");
        using var removed = await P0202Fixture.SendAsync(client, proof, path: path);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        await AssertCodeAsync(await P0202Fixture.SendAsync(client, proof, path: path), HttpStatusCode.Unauthorized, "Billing.DelegationReplayed");
        await AssertDeniedAsync(await P0202Fixture.SendAsync(client, delayedManagerRead,
            path: MerchantBillingDelegationV1.AccountAccessPath(scenario.Account)));
        Assert.Equal(1L, await scenario.CountAsync("SELECT count(*) FROM billing_member_removals"));
    }

    [Fact]
    public async Task EvidenceFailureRollsBackAndRestrictedRuntimeCannotChangeEvidenceOrMembers()
    {
        await using var scenario = await StartAsync();
        var bootstrap = database.Bootstrap(scenario.Fixture.Owner.Database);
        await LicensingPostgreSqlFixture.ExecuteAsync(bootstrap, """
            ALTER TABLE billing_member_removals ADD CONSTRAINT fixture_reject_reason CHECK (reason <> 'Fixture audit failure')
            """);
        using var failed = await scenario.RemoveAsync(reason: "Fixture audit failure");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(0L, await scenario.CountAsync("SELECT count(*) FROM merchant_billing_delegation_uses WHERE action = 'billing.member.remove'"));
        Assert.Equal(1L, await scenario.CountAsync("SELECT count(*) FROM billing_account_members WHERE role = 2 AND status = 1 AND revision = 1"));

        await LicensingPostgreSqlFixture.ExecuteAsync(bootstrap, "ALTER TABLE billing_member_removals DROP CONSTRAINT fixture_reject_reason");
        using var committed = await scenario.RemoveAsync(reason: "Fixture audit failure");
        Assert.Equal(HttpStatusCode.OK, committed.StatusCode);
        await AssertRestrictedRuntimeAsync(scenario);
        var immutable = await Assert.ThrowsAsync<PostgresException>(() =>
            LicensingPostgreSqlFixture.ExecuteAsync(bootstrap, "UPDATE billing_member_removals SET reason = 'Changed'"));
        Assert.Equal("55000", immutable.SqlState);
        Assert.Equal(1L, await scenario.CountAsync("SELECT count(*) FROM billing_member_removals WHERE reason = 'Fixture audit failure'"));
    }

    [Fact]
    public async Task QueuedConsentAfterRevocationRequiresReviewAndDoesNotApplyAnAffiliation()
    {
        using var key = RSA.Create(2048);
        using var capture = new P0205Tests.CaptureDelivery();
        await using var scenario = await StartAsync(capture, key);
        using var request = await scenario.Manager.PostAsJsonAsync(scenario.Path + "/affiliations",
            new { operationKey = Guid.NewGuid(), shopId = scenario.Shop, withoutCoverage = true });
        Assert.Equal(HttpStatusCode.Created, request.StatusCode);
        var requestId = (await ReadAsync(request)).GetProperty("requestId").GetGuid();
        var consent = $"{scenario.Path}/affiliations/{scenario.Shop:D}/{requestId:D}/consent";
        capture.Watch(requestId);
        await P0205Tests.DecideAsync(scenario.Manager, consent, "grant");
        Assert.Equal("Accepted", await P0205Tests.DecideAsync(scenario.Manager, consent, "accept"));
        await capture.Body.WaitAsync(TimeSpan.FromSeconds(15));

        using var removed = await scenario.RemoveAsync();
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        capture.Forward();
        await P0205Tests.WaitForConsentAsync(scenario.Manager, consent, "NeedsReview");
        Assert.Equal(0L, await scenario.CountAsync("SELECT count(*) FROM shop_affiliations"));
        Assert.Equal(1L, await scenario.CountAsync("SELECT count(*) FROM shop_affiliation_inbox WHERE status = 'NeedsReview'"));
        Assert.Equal(scenario.ShopAuthorities, await ShopAuthoritiesAsync(database));
    }

    private static async Task AssertRestrictedRuntimeAsync(Scenario scenario)
    {
        var runtime = scenario.Fixture.Runtime;
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(runtime, "SELECT count(*) FROM billing_member_removals"));
        await using var db = new NpgsqlConnection(runtime.ConnectionString);
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        await using var settings = new NpgsqlCommand("""
            SELECT set_config('app.billing_account_id', $1, true), set_config('app.billing_actor_id', $2, true)
            """, db, tx);
        settings.Parameters.AddWithValue(scenario.Account.ToString("D"));
        settings.Parameters.AddWithValue(scenario.ManagerActor.ToString("D"));
        await settings.ExecuteNonQueryAsync();
        await using var evidence = new NpgsqlCommand("SELECT count(*) FROM billing_member_removals", db, tx);
        Assert.Equal(0L, await evidence.ExecuteScalarAsync());
        await using var mutation = new NpgsqlCommand("UPDATE billing_account_members SET status = 1 WHERE id = $1", db, tx);
        mutation.Parameters.AddWithValue(scenario.Member);
        var refused = await Assert.ThrowsAsync<PostgresException>(() => mutation.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refused.SqlState);
    }

    private static P0202Fixture.ActorAssertion SignRemoval(Scenario scenario, string path)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new { operationKey = Guid.NewGuid(), expectedVersion = 1, reason = "Test removal" });
        return P0202Fixture.Sign(scenario.Fixture, scenario.OwnerActor, P0202Fixture.Create, body, claims =>
        {
            claims["action"] = BillingInvitationsV1.RemoveMember;
            claims["account_id"] = scenario.Account;
            claims["path"] = path;
        });
    }

    internal static async Task<NpgsqlTransaction> HoldAccountFenceAsync(NpgsqlConnection db, Guid account)
    {
        var transaction = await db.BeginTransactionAsync();
        await using var fence = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1, 0))", db, transaction);
        fence.Parameters.AddWithValue($"b{account:D}");
        await fence.ExecuteNonQueryAsync();
        return transaction;
    }

    internal static async Task WaitForLockAsync(NpgsqlConnection db, NpgsqlTransaction transaction, string type, int waiters = 1,
        params int[] backendPids)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2.5));
        await using var wait = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM pg_locks l JOIN pg_stat_activity a USING (pid)
                WHERE NOT l.granted AND l.locktype = $1 AND a.datname = current_database()
                    AND (cardinality($3) = 0 OR l.pid = ANY($3))) >= $2
            FROM pg_stat_clear_snapshot()
            """, db, transaction);
        wait.Parameters.AddWithValue(type);
        wait.Parameters.AddWithValue(waiters);
        wait.Parameters.AddWithValue(backendPids);
        while (!(bool)(await wait.ExecuteScalarAsync(deadline.Token))!)
            await Task.Delay(10, deadline.Token);
    }
}
