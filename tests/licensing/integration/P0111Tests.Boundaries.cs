using System.Net;
using System.Text.Json;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0111Tests
{
    [Fact]
    public async Task RecentMfaUsesTheFixedClockAndCurrentDeviceNeedsNoStepUp()
    {
        await using var fixture = await StartAsync();
        using var target = await fixture.OwnerAsync();
        var targetId = await fixture.LatestSessionAsync();
        using var other = await fixture.OwnerAsync();
        var otherId = await fixture.LatestSessionAsync();
        using var actor = await fixture.OwnerAsync();
        var actorId = await fixture.LatestSessionAsync();
        fixture.Clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromMilliseconds(1));
        using var justInside = await PostAsync(actor, Revoke(targetId), HttpStatusCode.NoContent);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1));
        using var boundary = await PostAsync(actor, Revoke(otherId), HttpStatusCode.Forbidden);
        using var json = JsonDocument.Parse(await boundary.Content.ReadAsStringAsync());
        Assert.Equal("Admin.RecentMfaRequired", json.RootElement.GetProperty("code").GetString());
        using var list = await GetAsync(actor, Sessions, HttpStatusCode.OK);
        Assert.Equal(2, list.RootElement.GetProperty("items").GetArrayLength());
        using var saved = actor.Fork(P0114Tests.RawClient(fixture.Api));
        using var current = await PostAsync(actor, Revoke(actorId), HttpStatusCode.NoContent);
        Assert.Null(actor[AccessCookie]);
        Assert.Null(actor[RefreshCookie]);
        await AssertEndedAsync(saved);
        await AssertSurvivesAsync(other);
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action=20 AND reason=19"));
    }

    [Fact]
    public async Task AuditFailureRollsBackRevocationAndDoesNotReleaseListOrCookies()
    {
        await using var fixture = await StartAsync();
        using var actor = await fixture.OwnerAsync();
        using var target = await fixture.OwnerAsync();
        var targetId = await fixture.LatestSessionAsync();
        var migrator = new NpgsqlConnectionStringBuilder(fixture.Runtime.ConnectionString)
            { Username = LicensingPostgreSqlFixture.MigratorRole };
        await LicensingPostgreSqlFixture.ExecuteAsync(migrator, """
            CREATE FUNCTION refuse_device_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.action BETWEEN 17 AND 19 THEN RAISE EXCEPTION 'p0111-audit-storage-canary'; END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER refuse_device_audit BEFORE INSERT ON provider_audit_events
            FOR EACH ROW EXECUTE FUNCTION refuse_device_audit();
            """);
        using var list = await GetAsync(actor, Sessions, HttpStatusCode.ServiceUnavailable);
        Assert.False(list.RootElement.TryGetProperty("items", out _));
        using var revoke = await PostAsync(actor, Revoke(targetId), HttpStatusCode.ServiceUnavailable);
        using var signOut = await PostAsync(target, SignOut, HttpStatusCode.ServiceUnavailable);
        Assert.False(signOut.Headers.Contains("Set-Cookie"));
        Assert.DoesNotContain("p0111-audit-storage-canary", await revoke.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await AssertSurvivesAsync(target);
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM admin_sessions WHERE revoked_at IS NOT NULL"));
    }

    [Fact]
    public async Task OwnListOmitsIdleAbsoluteAndFutureMfaSessionsAtFixedBoundaries()
    {
        await using var fixture = await StartAsync();
        using var idle = await fixture.OwnerAsync();
        fixture.Clock.Advance(TimeSpan.FromMinutes(29) + TimeSpan.FromSeconds(30));
        using var actor = await fixture.OwnerAsync();
        var actorId = await fixture.LatestSessionAsync();
        using var future = await fixture.OwnerAsync();
        fixture.Clock.Advance(TimeSpan.FromSeconds(-30));
        var insideId = Guid.NewGuid();
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Runtime, $"""
            INSERT INTO admin_sessions (id, admin_id, created_at, mfa_verified_at, last_seen_at)
            SELECT gen_random_uuid(), admin_id, created_at - interval '12 hours',
                created_at - interval '12 hours', created_at
            FROM admin_sessions WHERE id = '{actorId}';
            INSERT INTO admin_sessions (id, admin_id, created_at, mfa_verified_at, last_seen_at)
            SELECT '{insideId}', admin_id, created_at - interval '12 hours' + interval '1 millisecond',
                created_at - interval '12 hours' + interval '1 millisecond', created_at
            FROM admin_sessions WHERE id = '{actorId}';
            """);
        using var list = await GetAsync(actor, Sessions, HttpStatusCode.OK);
        Assert.Equal(new[] { actorId, insideId },
            list.RootElement.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task SignOutWithAConsumedCredentialEndsTheFamilyWithoutReplayingRotation()
    {
        await using var fixture = await StartAsync();
        using var original = await fixture.OwnerAsync();
        using var holder = original.Fork(P0114Tests.RawClient(fixture.Api));
        using var rotated = await PostAsync(original, Refresh, HttpStatusCode.OK);
        holder.Forget(AccessCookie);
        using var signedOut = await PostAsync(holder, SignOut, HttpStatusCode.NoContent);
        await AssertEndedAsync(original);
        Assert.Equal(2L, await fixture.ScalarAsync("SELECT count(*) FROM admin_refresh_credentials"));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action=16"));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action=19"));
    }

    [Fact]
    public async Task SignOutWithValidAccessAndNoRefreshCookieEndsTheAccessSession()
    {
        await using var fixture = await StartAsync();
        using var sibling = await fixture.OwnerAsync();
        using var target = await fixture.OwnerAsync();
        using var saved = target.Fork(P0114Tests.RawClient(fixture.Api));
        target.Forget(RefreshCookie);
        using var signedOut = await PostAsync(target, SignOut, HttpStatusCode.NoContent);
        Assert.Null(target[AccessCookie]);
        await AssertEndedAsync(saved);
        await AssertSurvivesAsync(sibling);
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action=19"));
    }

}
