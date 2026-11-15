using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Audit;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.AdminIdentities.Mfa;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities;

/// <summary>
/// Recovery persistence. The administrator row is locked first (the sign-in, refresh and MFA order),
/// so a recovery serializes with every other credential change for that administrator.
/// </summary>
internal sealed class AdminRecoveryStore(LicensingDbContext context, AdminMfaStore mfa, AdminMfaKeys keys,
    AuditEvidenceFactory audit) : IAdminRecoveryStore
{
    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", cancellationToken);
        var result = await operation(cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<AdminIdentity?> LockByEmailAsync(string? normalizedEmail, CancellationToken cancellationToken)
    {
        if (normalizedEmail is null) return null;
        var rows = await context.Set<AdminIdentity>().FromSql($"""
            SELECT *, xmin FROM public.admin_identities WHERE email = {normalizedEmail} FOR UPDATE
            """).ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public async Task<(AdminIdentity? Reviewer, AdminIdentity? Target)> LockReviewAsync(Guid reviewerId,
        string normalizedEmail, CancellationToken cancellationToken)
    {
        var rows = await context.Set<AdminIdentity>().FromSql($"""
            SELECT *, xmin FROM public.admin_identities
            WHERE id = {reviewerId} OR email = {normalizedEmail} ORDER BY id FOR UPDATE
            """).ToListAsync(cancellationToken);
        return (rows.SingleOrDefault(row => row.Id == reviewerId),
            rows.SingleOrDefault(row => string.Equals(row.Email, normalizedEmail, StringComparison.Ordinal)));
    }

    public async Task<AdminSession?> LockReviewerSessionAsync(AdminSessionCaller caller, CancellationToken cancellationToken)
    {
        var rows = await context.Set<AdminSession>().FromSql($"""
            SELECT *, xmin FROM public.admin_sessions WHERE id = {caller.SessionId} AND admin_id = {caller.AdminId}
            FOR UPDATE
            """).ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public async Task<RecoveryCodeUse> ConsumeRecoveryCodeAsync(Guid adminId, string normalizedCode, bool hasContinuation,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var totp = await mfa.ReadTotpAsync(adminId, cancellationToken);
        if (totp is null || keys.HashRecovery(totp, normalizedCode) is not { } digest) return RecoveryCodeUse.Invalid;
        var code = await mfa.ReadRecoveryAsync(adminId, digest, cancellationToken);
        if (code is null) return RecoveryCodeUse.Invalid;
        var final = !await context.Set<AdminRecoveryCode>().AnyAsync(candidate =>
            candidate.AdminId == adminId && candidate.Digest != digest && candidate.ConsumedAt == null, cancellationToken);
        if (final && !hasContinuation) return RecoveryCodeUse.ContinuationRequired;
        code.Consume(now);
        return final ? RecoveryCodeUse.FinalCodeConsumed : RecoveryCodeUse.Consumed;
    }

    public async Task<AdminRecovery?> LockResetAsync(Guid adminId, string grantDigest, CancellationToken cancellationToken)
    {
        var rows = await context.Set<AdminRecovery>().FromSql($"""
            SELECT *, xmin FROM public.admin_recoveries
            WHERE admin_id = {adminId} AND grant_digest = {grantDigest} FOR UPDATE
            """).ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public Task<bool> IsPlatformOwnerAsync(Guid adminId, CancellationToken cancellationToken) =>
        context.Set<InitialAdminBootstrap>().AnyAsync(state => state.OwnerId == adminId && state.ConsumedAt != null,
            cancellationToken);

    public Task<int> CountRecoverableOwnersAsync(Guid exceptAdminId, CancellationToken cancellationToken) =>
        (from state in context.Set<InitialAdminBootstrap>()
         join owner in context.Set<AdminIdentity>() on state.OwnerId equals owner.Id
         where state.ConsumedAt != null && owner.Id != exceptAdminId
             && owner.MfaEnrolledAt != null && owner.DeactivatedAt == null
         select owner.Id).CountAsync(cancellationToken);

    public async Task ResetMfaAsync(AdminIdentity admin, DateTimeOffset now, bool keepRecoveryCodes,
        CancellationToken cancellationToken)
    {
        var sessions = await context.Set<AdminSession>().FromSql($"""
            SELECT *, xmin FROM public.admin_sessions WHERE admin_id = {admin.Id} AND revoked_at IS NULL
            ORDER BY id FOR UPDATE
            """).ToListAsync(cancellationToken);
        foreach (var session in sessions)
            await AdminSessionRevocation.StageAsync(context, audit, session, now, BootstrapAuditAction.DeviceSessionRevoked,
                AuditReason.RecoveryReset, cancellationToken, AuditActor.System);

        (await mfa.ReadTotpAsync(admin.Id, cancellationToken))?.Retire(now);
        if (!keepRecoveryCodes) await mfa.VoidRecoveryCodesAsync(admin.Id, now, cancellationToken);
        await context.Set<AdminRecovery>()
            .Where(open => open.AdminId == admin.Id && open.Kind == AdminRecoveryKind.ReviewedReset
                && open.RedeemedAt == null && open.RetiredAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(open => open.RetiredAt, now), cancellationToken);
        await RevokeEnrollmentAsync(admin.Id, now, cancellationToken);
        admin.ResetMfa();
    }

    public Task RevokeEnrollmentAsync(Guid adminId, DateTimeOffset now, CancellationToken cancellationToken) =>
        context.Set<BootstrapEnrollmentCredential>()
            .Where(credential => credential.OwnerId == adminId && credential.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(credential => credential.RevokedAt, now), cancellationToken);

    public void Add(AdminRecovery recovery) => context.Add(recovery);
    public void Add(BootstrapEnrollmentCredential credential) => context.Add(credential);
    public void Add(ProviderAuditEvent auditEvent) => context.Add(auditEvent);
}
