using System.Net;
using System.Text.Json;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0112Tests
{
    private const string EvidenceReference = "SEC-2026-0412";

    internal static object ReviewedReset(string email, bool confirmed = true, string reason = "authenticator-lost") => new
    {
        email,
        reason,
        verification = "video-call",
        evidenceReference = EvidenceReference,
        confirmed,
    };

    [Fact]
    public async Task ReviewedResetNeedsRecentMfaEvidenceAndYieldsOneUseEnrollmentOnly()
    {
        await using var fixture = await StartAsync();
        using var owner = await fixture.OwnerAsync();
        var inviteeSeed = await fixture.InviteeSeedAsync(owner);
        using var invitee = await P0114Tests.SignInAsync(fixture.Api, fixture.Clock, P0114Tests.Invitee,
            P0114Tests.InviteePassword, inviteeSeed);

        using var notOwner = await PostAsync(invitee, Resets, ReviewedReset(OwnerEmail));
        Assert.Equal(HttpStatusCode.Forbidden, notOwner.StatusCode);
        using var unconfirmed = await PostAsync(owner, Resets, ReviewedReset(P0114Tests.Invitee, confirmed: false));
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        using var unknownReason = await PostAsync(owner, Resets, ReviewedReset(P0114Tests.Invitee, reason: "because"));
        Assert.Equal(HttpStatusCode.BadRequest, unknownReason.StatusCode);
        using var unknownTarget = await PostAsync(owner, Resets, ReviewedReset("nobody@example.test"));
        Assert.Equal(HttpStatusCode.NotFound, unknownTarget.StatusCode);
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        using var stale = await PostAsync(owner, Resets, ReviewedReset(P0114Tests.Invitee));
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Equal("Admin.RecentMfaRequired", await CodeOfAsync(stale));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM admin_recoveries"));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(invitee, HttpMethod.Get, Session));

        using var reviewer = await fixture.OwnerAsync();
        using var issued = await PostAsync(reviewer, Resets, ReviewedReset(P0114Tests.Invitee));
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        using var grant = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        var token = grant.RootElement.GetProperty("resetToken").GetString()!;
        Assert.True(grant.RootElement.GetProperty("expiresAt").GetDateTimeOffset() > fixture.Clock.GetUtcNow());

        await AssertEndedAsync(invitee);
        Assert.Equal(HttpStatusCode.Unauthorized,
            await fixture.SignInStatusAsync(P0114Tests.Invitee, P0114Tests.InviteePassword, inviteeSeed));
        await RedeemResetAsync(fixture, token, inviteeSeed);
        await AssertAuditSafeAsync(fixture, reviewer, "recovery-reset-issued", token, P0114Tests.InviteePassword, EvidenceReference);
    }

    private static async Task RedeemResetAsync(Fixture fixture, string token, string oldSeed)
    {
        using var wrongPassword = await PostAsync(fixture.Browser(), RecoveryReset,
            new { email = P0114Tests.Invitee, password = "Wrong-password-0000", resetToken = token });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);

        using var recovering = fixture.Browser();
        using var redeemed = await PostAsync(recovering, RecoveryReset,
            new { email = P0114Tests.Invitee, password = P0114Tests.InviteePassword, resetToken = token });
        Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(recovering, HttpMethod.Get, Business));
        using var replay = await PostAsync(fixture.Browser(), RecoveryReset,
            new { email = P0114Tests.Invitee, password = P0114Tests.InviteePassword, resetToken = token });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        var (seed, codes) = await EnrollAsync(recovering, fixture.Clock);
        Assert.NotEqual(oldSeed, seed);
        Assert.Equal(10, codes.Length);
        Assert.Equal(HttpStatusCode.OK, await fixture.SignInStatusAsync(P0114Tests.Invitee, P0114Tests.InviteePassword, seed));
        Assert.Equal(1L, await fixture.ScalarAsync("""
            SELECT count(*) FROM admin_recoveries WHERE kind = 2 AND redeemed_at IS NOT NULL
              AND reviewer_id IS NOT NULL AND evidence_reference = 'SEC-2026-0412'
            """));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action = 23 AND reason = 28"));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action = 24 AND reason = 29"));
    }
}
