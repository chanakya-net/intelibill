using System.Net;
using Intelibill.Licensing.Application.AdminIdentities;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0112Tests
{
    [Fact]
    public async Task ReviewRevalidatesTheAuthenticatedSessionBeforeChangingTheTarget()
    {
        var gate = new ReviewGate();
        await using var fixture = await StartAsync(services => services.AddSingleton<IAdminSignInBudget>(gate));
        using var owner = await fixture.OwnerAsync();
        var seed = await fixture.InviteeSeedAsync(owner);
        using var target = await P0114Tests.SignInAsync(fixture.Api, fixture.Clock, P0114Tests.Invitee,
            P0114Tests.InviteePassword, seed);
        using var ending = owner.Fork(P0114Tests.RawClient(fixture.Api));
        gate.Arm();
        var mutation = PostAsync(owner, Resets, ReviewedReset(P0114Tests.Invitee));
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var signedOut = await PostAsync(ending, "/api/admin/auth/sign-out", new { });
            Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        }
        finally
        {
            gate.Release.TrySetResult();
        }

        using var denied = await mutation;
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal("Admin.SessionEnded", await CodeOfAsync(denied));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM admin_recoveries"));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM admin_identities WHERE mfa_enrolled_at IS NULL"));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action = 25 AND reason = 18"));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(target, HttpMethod.Get, Session));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(target, HttpMethod.Post, Refresh));
    }

    private sealed class ReviewGate : IAdminSignInBudget
    {
        private int _armed;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Arm() => Interlocked.Exchange(ref _armed, 1);

        public async Task<DateTimeOffset?> TakeAsync(string accountSubject, DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 0) return null;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return null;
        }
    }

    [Fact]
    public async Task RecoveryEvidenceIsImmutableUnderTheRestrictedRuntimeRole()
    {
        await using var fixture = await StartAsync();
        using var owner = await fixture.OwnerAsync();
        var seed = await fixture.InviteeSeedAsync(owner);
        using var first = await PostAsync(owner, Resets, ReviewedReset(P0114Tests.Invitee));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        await AssertDatabaseRefusalAsync(fixture, "UPDATE admin_recoveries SET evidence_reference = 'SEC-REWRITE'", "55000");
        await AssertDatabaseRefusalAsync(fixture, "DELETE FROM admin_recoveries", "55000");
        await AssertDatabaseRefusalAsync(fixture, "TRUNCATE admin_recoveries", "42501");

        using var second = await PostAsync(owner, Resets, ReviewedReset(P0114Tests.Invitee, reason: "suspected-compromise"));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        await RedeemLatestGrantAsync(fixture, body.RootElement.GetProperty("resetToken").GetString()!, seed);
        await AssertDatabaseRefusalAsync(fixture, "UPDATE admin_recoveries SET retired_at = NULL WHERE retired_at IS NOT NULL", "55000");
        await AssertDatabaseRefusalAsync(fixture, "UPDATE admin_recoveries SET redeemed_at = NULL WHERE redeemed_at IS NOT NULL", "55000");
        Assert.Equal(2L, await fixture.ScalarAsync("SELECT count(*) FROM admin_recoveries WHERE evidence_reference = 'SEC-2026-0412'"));
    }

    [Fact]
    public async Task RecoveredOwnerHasNoRetiredSecretOrOrdinaryEnrollmentCredential()
    {
        await using var fixture = await StartAsync();
        using var browser = fixture.Browser();
        using var recovered = await PostAsync(browser, RecoveryCode,
            new { email = OwnerEmail, password = fixture.Password, recoveryCode = fixture.Codes[0] });
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(true, await fixture.ScalarAsync("""
            SELECT retired_at IS NOT NULL AND encrypted_secret = decode(repeat('00', 48), 'hex')
            FROM admin_totp
            """));
        await AssertDatabaseRefusalAsync(fixture, """
            INSERT INTO bootstrap_enrollment_credentials (id, owner_id, digest, issued_at, expires_at, revoked_at)
            SELECT gen_random_uuid(), id, repeat('A', 64), now(), now() + interval '15 minutes', now()
            FROM admin_identities
            """, "55000");
        Assert.Equal(1L, await fixture.ScalarAsync("""
            SELECT count(*) FROM bootstrap_enrollment_credentials WHERE issued_for_recovery AND revoked_at IS NULL
            """));
        var (seed, _) = await EnrollAsync(browser, fixture.Clock);
        Assert.NotEqual(fixture.Seed, seed);
    }

    private static async Task RedeemLatestGrantAsync(Fixture fixture, string token, string oldSeed)
    {
        using var browser = fixture.Browser();
        using var response = await PostAsync(browser, RecoveryReset,
            new { email = P0114Tests.Invitee, password = P0114Tests.InviteePassword, resetToken = token });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (seed, _) = await EnrollAsync(browser, fixture.Clock);
        Assert.NotEqual(oldSeed, seed);
    }

    private static async Task AssertDatabaseRefusalAsync(Fixture fixture, string sql, string sqlState)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => LicensingPostgreSqlFixture.ExecuteAsync(fixture.Runtime, sql));
        Assert.Equal(sqlState, error.SqlState);
    }
}
