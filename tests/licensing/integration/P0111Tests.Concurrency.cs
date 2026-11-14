using System.Net;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0111Tests
{
    [Fact]
    public async Task ConcurrentRotationAndRevocationLeaveNoLiveTargetAcrossReplicas()
    {
        await using var fixture = await StartAsync();
        using var actor = await fixture.OwnerAsync();
        using var target = await fixture.OwnerAsync();
        var targetId = await fixture.LatestSessionAsync();
        using var sibling = await fixture.OwnerAsync();
        await using var replica = new LicensingApiFactory(fixture.Runtime, clock: fixture.Clock, settings: fixture.Settings);
        using var rotating = target.Fork(P0114Tests.RawClient(replica));
        var refreshProof = await ReadProofAsync(rotating);
        var revokeProof = await ReadProofAsync(actor);
        await using var gate = new NpgsqlConnection(fixture.Runtime.ConnectionString);
        await gate.OpenAsync();
        await using var transaction = await gate.BeginTransactionAsync();
        await using var locked = new NpgsqlCommand("SELECT id FROM admin_identities FOR UPDATE", gate, transaction);
        await locked.ExecuteNonQueryAsync();

        var refresh = rotating.SendAsync(HttpMethod.Post, Refresh, csrf: refreshProof);
        var revoke = actor.SendAsync(HttpMethod.Post, Revoke(targetId), new { reason = "device-unused" }, revokeProof);
        try { await WaitForIdentityLockAsync(fixture, 2); }
        finally { await transaction.RollbackAsync(); }

        using var refreshed = await refresh;
        using var revoked = await revoke;
        Assert.True(refreshed.StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        await AssertEndedAsync(target);
        await AssertEndedAsync(rotating);
        await AssertSurvivesAsync(sibling);
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action=18"));
        Assert.Equal(refreshed.StatusCode == HttpStatusCode.OK ? 1L : 0L, await fixture.ScalarAsync($"""
            SELECT count(*) FROM admin_refresh_credentials WHERE session_id = '{targetId}' AND parent_id IS NOT NULL
            """));
    }

    [Fact]
    public async Task CallerRevokedDuringTheLockWaitCannotRevokeAnotherDevice()
    {
        await using var fixture = await StartAsync();
        using var actor = await fixture.OwnerAsync();
        var actorId = await fixture.LatestSessionAsync();
        using var target = await fixture.OwnerAsync();
        var targetId = await fixture.LatestSessionAsync();
        var proof = await ReadProofAsync(actor);
        await using var gate = new NpgsqlConnection(fixture.Runtime.ConnectionString);
        await gate.OpenAsync();
        await using var transaction = await gate.BeginTransactionAsync();
        await using var locked = new NpgsqlCommand("SELECT id FROM admin_identities FOR UPDATE", gate, transaction);
        await locked.ExecuteNonQueryAsync();

        var revoke = actor.SendAsync(HttpMethod.Post, Revoke(targetId), new { reason = "device-unused" }, proof);
        await WaitForIdentityLockAsync(fixture, 1);
        await using var cutOff = new NpgsqlCommand(
            "UPDATE admin_sessions SET revoked_at=last_seen_at WHERE id=$1", gate, transaction);
        cutOff.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = actorId });
        await cutOff.ExecuteNonQueryAsync();
        await transaction.CommitAsync();

        using var refused = await revoke;
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        await AssertSurvivesAsync(target);
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action=18"));
    }

    private async Task WaitForIdentityLockAsync(Fixture fixture, long expected)
    {
        var monitor = database.Bootstrap(LicensingPostgreSqlFixture.LicensingDatabase);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!deadline.IsCancellationRequested)
        {
            var current = await LicensingPostgreSqlFixture.ReadScalarAsync(monitor, """
                SELECT count(*) FROM pg_stat_activity WHERE usename='licensing_runtime'
                    AND wait_event_type='Lock' AND query LIKE '%admin_identities%FOR UPDATE%'
                """);
            if (current is long count && count >= expected) return;
            await Task.Delay(10, deadline.Token);
        }

        Assert.Fail($"Expected {expected} competing identity lock request(s) in PostgreSQL.");
    }

    private static async Task<string> ReadProofAsync(P0110CookieBrowser browser)
    {
        using var context = await GetAsync(browser, "/api/admin/auth/sign-in/context", HttpStatusCode.OK);
        return context.RootElement.GetProperty("requestToken").GetString()!;
    }
}
