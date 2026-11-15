using System.Net;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0113Tests
{
    [Fact]
    public async Task OwnerRecoveringWithCodeCannotBeCountedAsBackup()
    {
        await using var fixture = await StartAsync();
        using var first = await fixture.SignInOwnerAsync();
        var seed = await fixture.EnrollOperatorAsync(first);
        var candidate = await FindAsync(first, OperatorEmail);
        using var promoted = await UpdateAsync(first, candidate, [OwnerGrant]);
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        using var second = await fixture.SignInOperatorAsync(seed);
        using var recovery = new P0110CookieBrowser(P0114Tests.RawClient(fixture.Api));

        var (recovered, reduced) = await RaceRecoveryAsync(fixture, recovery, second, await FindAsync(second, OperatorEmail));
        Assert.Equal(HttpStatusCode.OK, recovered.Status);
        Assert.Equal((HttpStatusCode.Conflict, "Admin.LastRecoverableOwner"), reduced);
        await AssertEndedAsync(first);
        using var enrollment = await recovery.SendAsync(HttpMethod.Get, "/api/admin/auth/bootstrap/enrollment/mfa");
        Assert.Equal(HttpStatusCode.OK, enrollment.StatusCode);
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM admin_recoveries WHERE kind=1"));
        Assert.Equal(1L, await fixture.ScalarAsync("""
            SELECT count(*) FROM admin_identities
            WHERE capabilities & 16 = 16 AND deactivated_at IS NULL AND mfa_enrolled_at IS NOT NULL
            """));
        using var surviving = await second.SendAsync(HttpMethod.Get, Users);
        Assert.Equal(HttpStatusCode.OK, surviving.StatusCode);
    }

    [Fact]
    public async Task DelegatedOwnerCanInviteAndReviewRecovery()
    {
        await using var fixture = await StartAsync();
        using var first = await fixture.SignInOwnerAsync();
        var seed = await fixture.EnrollOperatorAsync(first);
        var candidate = await FindAsync(first, OperatorEmail);
        using var promoted = await UpdateAsync(first, candidate, [OwnerGrant]);
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        using var delegated = await fixture.SignInOperatorAsync(seed);
        await P0114Tests.IssueAsync(delegated, "delegated-invitation@example.test");
        using var review = await delegated.SendAsync(HttpMethod.Post, "/api/admin/recovery/resets", new
        {
            email = BootstrapEmail, reason = "authenticator-lost", verification = "video-call",
            evidenceReference = "SEC-843", confirmed = true,
        }, await ProofAsync(delegated));
        Assert.Equal(HttpStatusCode.Created, review.StatusCode);
        await AssertEndedAsync(first);
        var owner = await FindAsync(delegated, OperatorEmail);
        using var refused = await SendUpdateAsync(delegated, owner, [OwnerGrant], await ProofAsync(delegated), disable: true);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("Admin.LastRecoverableOwner", (await ReadBodyAsync(refused)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task RevokingCallerAfterHttpAuthorizationRefusesTheMutation()
    {
        await using var fixture = await StartAsync();
        using var owner = await fixture.SignInOwnerAsync();
        await fixture.EnrollOperatorAsync(owner);
        var target = await FindAsync(owner, OperatorEmail);
        var proof = await ProofAsync(owner);
        await using var gate = new NpgsqlConnection(fixture.Runtime.ConnectionString);
        await gate.OpenAsync();
        await using var tx = await gate.BeginTransactionAsync();
        await LockFenceAsync(gate, tx);

        var update = SendUpdateAsync(owner, target, ["billing"], proof);
        await WaitForFenceAsync(fixture, 1);
        await using var revoke = new NpgsqlCommand("UPDATE admin_sessions SET revoked_at=last_seen_at", gate, tx);
        await revoke.ExecuteNonQueryAsync();
        await tx.CommitAsync();

        Assert.Equal((HttpStatusCode.Unauthorized, "Admin.SessionEnded"), await ReadOutcomeAsync(update));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM admin_identities WHERE capabilities=2"));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action=105"));
    }

    [Fact]
    public async Task AuditInsertFailureRollsBackGrantsAndSessionRevocation()
    {
        await using var fixture = await StartAsync();
        using var owner = await fixture.SignInOwnerAsync();
        var seed = await fixture.EnrollOperatorAsync(owner);
        using var member = await fixture.SignInOperatorAsync(seed);
        var target = await FindAsync(owner, OperatorEmail);
        var admin = database.Bootstrap(fixture.Runtime.Database);
        await LicensingPostgreSqlFixture.ExecuteAsync(admin, """
            CREATE FUNCTION refuse_capability_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW.action=105 THEN RAISE EXCEPTION 'fixture-audit-failure' USING ERRCODE='55000'; END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER refuse_capability_audit BEFORE INSERT ON provider_audit_events
            FOR EACH ROW EXECUTE FUNCTION refuse_capability_audit();
            """);

        using var failed = await UpdateAsync(owner, target, ["support"]);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        var failure = await ReadBodyAsync(failed);
        Assert.Equal("Internal.Unexpected", failure.GetProperty("code").GetString());
        Assert.DoesNotContain("fixture-audit-failure", failure.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(target, await FindAsync(owner, OperatorEmail));
        using var session = await member.SendAsync(HttpMethod.Get, Session);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.Empty(Grants(await ReadBodyAsync(session)));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action=105 OR reason=36"));
    }

    [Fact]
    public async Task FuturePersistedMfaProofDoesNotAuthorizeTheEditor()
    {
        await using var fixture = await StartAsync();
        using var owner = await fixture.SignInOwnerAsync();
        var target = await FindAsync(owner, BootstrapEmail);
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Runtime,
            "UPDATE admin_sessions SET mfa_verified_at=mfa_verified_at+interval '1 second'");
        using var update = await UpdateAsync(owner, target, ["support", OwnerGrant]);
        Assert.Equal(HttpStatusCode.Unauthorized, update.StatusCode);
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action=105"));
    }

    private async Task<((HttpStatusCode Status, string? Code) Recovery, (HttpStatusCode Status, string? Code) Demotion)>
        RaceRecoveryAsync(Fixture fixture, P0110CookieBrowser recovery, P0110CookieBrowser second, Target secondRow)
    {
        var recoveryProof = await ProofAsync(recovery);
        var ownerProof = await ProofAsync(second);
        await using var gate = new NpgsqlConnection(fixture.Runtime.ConnectionString);
        await gate.OpenAsync();
        await using var tx = await gate.BeginTransactionAsync();
        await LockFenceAsync(gate, tx);

        var recover = recovery.SendAsync(HttpMethod.Post, "/api/admin/auth/recovery/code",
            new { email = BootstrapEmail, password = fixture.Password, recoveryCode = fixture.Codes[0] }, recoveryProof);
        await WaitForFenceAsync(fixture, 1);
        var demote = SendUpdateAsync(second, secondRow, [], ownerProof);
        try { await WaitForFenceAsync(fixture); }
        finally { await tx.RollbackAsync(); }
        return (await ReadOutcomeAsync(recover), await ReadOutcomeAsync(demote));
    }

    private static async Task LockFenceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        await using var locked = new NpgsqlCommand("SELECT id FROM initial_admin_bootstrap WHERE id=1 FOR UPDATE",
            connection, transaction);
        await locked.ExecuteNonQueryAsync();
    }
}
