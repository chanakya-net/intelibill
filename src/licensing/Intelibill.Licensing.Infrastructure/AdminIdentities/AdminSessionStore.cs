using Intelibill.Licensing.Application.Audit;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.AdminIdentities.Mfa;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities;

internal sealed class AdminSessionStore(LicensingDbContext context, AdminMfaStore mfa, AdminMfaKeys keys,
    AdminRefreshCredentialService refresh, TimeProvider clock, AuditEvidenceFactory audit)
    : IAdminSessionStore
{
    public async Task<ErrorOr<AdminSessionGrant>> OpenAsync(string? normalizedEmail, Func<string?, bool> passwordMatches,
        string? otp, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", cancellationToken);
        var rows = await context.Set<AdminIdentity>().FromSql($"""
            SELECT *, xmin FROM public.admin_identities WHERE email = {normalizedEmail} FOR UPDATE
            """).ToListAsync(cancellationToken);
        var admin = rows.SingleOrDefault();
        var matches = passwordMatches(admin?.PasswordHash);
        var now = clock.GetUtcNow();
        var result = await TryOpenAsync(admin, matches, otp, now, cancellationToken);
        context.Add(result.IsError
            ? audit.Create(BootstrapAuditAction.SignInRefused, now, AuditActor.Unauthenticated,
                AuditReason.CredentialsRefused, admin?.Id)
            : audit.Create(BootstrapAuditAction.SignInSucceeded, now, AuditActor.Administrator,
                AuditReason.CredentialsVerified, admin!.Id));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<ErrorOr<AdminSessionGrant>> TryOpenAsync(AdminIdentity? admin, bool matches, string? otp,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!matches || admin?.MfaEnrolledAt is null || admin.DeactivatedAt is not null)
            return AdminSignInHandler.Refused;
        var totp = await mfa.ReadTotpAsync(admin.Id, cancellationToken);
        if (totp?.LastAcceptedStep is null) return AdminSignInHandler.Refused;
        if (AdminMfaVerifier.CheckBudget(totp, now) is { } limited) return limited;
        var proof = AdminMfaVerifier.ConsumeStep(keys, totp, otp, now);
        if (proof.IsError)
            return proof.FirstError.Type == ErrorType.Failure ? proof.FirstError : AdminSignInHandler.Refused;
        var session = new AdminSession(Guid.NewGuid(), admin.Id, now);
        context.Add(session);
        return new AdminSessionGrant(session, refresh.IssueRoot(session), now);
    }

    public async Task<AdminSessionAccess?> AuthorizeAsync(Guid adminId, Guid sessionId, DateTimeOffset accessExpiresAt,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (now >= accessExpiresAt) return null;
        var active = await context.Database.SqlQuery<DateTimeOffset>($"""
            UPDATE public.admin_sessions AS session
            SET last_seen_at = GREATEST(session.last_seen_at, {now})
            FROM public.admin_identities AS admin
            WHERE session.id = {sessionId} AND session.admin_id = {adminId}
              AND admin.id = session.admin_id AND admin.deactivated_at IS NULL AND admin.mfa_enrolled_at IS NOT NULL
              AND session.revoked_at IS NULL AND session.mfa_verified_at <= {now}
              AND session.created_at > {now - AdminSession.AbsoluteLifetime}
              AND session.last_seen_at > {now - AdminSession.IdleLifetime}
            RETURNING session.mfa_verified_at AS "Value"
            """).ToListAsync(cancellationToken);
        if (active.Count == 0) return null;

        var isOwner = await context.Set<InitialAdminBootstrap>().AnyAsync(state =>
            state.OwnerId == adminId && state.ConsumedAt != null, cancellationToken);
        return new AdminSessionAccess(adminId, active[0], isOwner
            ? [ProviderActor.InvitationCapability, ProviderActor.AuditIdentityReadCapability] : []);
    }
}
