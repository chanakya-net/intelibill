using Intelibill.Licensing.Application.Audit;
using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.Data;
using System.Buffers.Text;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities;

/// <summary>
/// Single-use refresh rotation for provider sessions. Rotation locks the administrator, then the
/// session, then the presented credential (the sign-in order), so concurrent presentations on any
/// replica serialize. A consumed credential presented again revokes the session: neither holder
/// keeps a live successor.
/// </summary>
internal sealed class AdminRefreshCredentialService(LicensingDbContext context, TimeProvider clock,
    AuditEvidenceFactory audit) : IAdminRefreshCredentials
{
    private const string Prefix = "r1_";
    private const int EncodedLength = 46;

    /// <summary>Stages the first credential of a new session in the caller's transaction.</summary>
    internal string IssueRoot(AdminSession session) => Issue(session, parentId: null, session.CreatedAt);

    public async Task<ErrorOr<AdminSessionGrant>> RotateAsync(string? presented, CancellationToken cancellationToken)
    {
        if (!IsCredential(presented)) return AdminRefreshErrors.SessionEnded;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var locked = await LockCredentialAsync(Digest(presented!), cancellationToken);
        if (locked is null) return AdminRefreshErrors.SessionEnded;

        var result = Decide(locked.Admin, locked.Session, locked.Credential, clock.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<bool> EndAsync(string? presented, CancellationToken cancellationToken)
    {
        if (!IsCredential(presented)) return false;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var locked = await LockCredentialAsync(Digest(presented!), cancellationToken);
        if (locked is null) return false;
        await AdminSessionRevocation.StageAsync(context, audit, locked.Session, clock.GetUtcNow(),
            BootstrapAuditAction.DeviceSignedOut, AuditReason.SignedOut, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<LockedCredential?> LockCredentialAsync(string digest, CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", cancellationToken);
        var owner = await FindOwnerAsync(digest, cancellationToken);
        if (owner is null) return null;

        var admin = await LockAsync(context.Set<AdminIdentity>().FromSql($"""
            SELECT *, xmin FROM public.admin_identities WHERE id = {owner.AdminId} FOR UPDATE
            """), cancellationToken);
        var session = await LockAsync(context.Set<AdminSession>().FromSql($"""
            SELECT *, xmin FROM public.admin_sessions WHERE id = {owner.SessionId} FOR UPDATE
            """), cancellationToken);
        var credential = await LockAsync(context.Set<AdminRefreshCredential>().FromSql($"""
            SELECT * FROM public.admin_refresh_credentials WHERE session_id = {owner.SessionId}
              AND token_hash = {digest} FOR UPDATE
            """), cancellationToken);
        return admin is null || session is null || credential is null ? null : new(admin, session, credential);
    }

    private static bool IsCredential(string? presented) =>
        presented is { Length: EncodedLength } && presented.StartsWith(Prefix, StringComparison.Ordinal)
        && Base64Url.IsValid(presented.AsSpan(Prefix.Length));

    private ErrorOr<AdminSessionGrant> Decide(AdminIdentity admin, AdminSession session, AdminRefreshCredential credential,
        DateTimeOffset now)
    {
        if (credential.IsConsumed)
        {
            RevokeForReuse(session, now);
            return AdminRefreshErrors.SessionEnded;
        }
        if (admin.DeactivatedAt is not null || admin.MfaEnrolledAt is null || !session.IsLiveAt(now) || credential.ExpiresAt <= now)
            return AdminRefreshErrors.SessionEnded;

        credential.Consume(now);
        session.Touch(now);
        return new AdminSessionGrant(session, Issue(session, credential.Id, now), now);
    }

    private void RevokeForReuse(AdminSession session, DateTimeOffset now)
    {
        if (session.RevokedAt is not null) return;
        var before = session.IsLiveAt(now) ? AuditState.Active : AuditState.Expired;
        session.Revoke(now);
        context.Add(audit.Create(BootstrapAuditAction.SessionReuseRevoked, now, AuditActor.System,
            AuditReason.RefreshCredentialReused, session.AdminId,
            before: new(AuditTarget.Access, session.AdminId, before, MilestoneAt: session.CreatedAt),
            after: new(AuditTarget.Access, session.AdminId, AuditState.Revoked, MilestoneAt: session.RevokedAt)));
    }

    private string Issue(AdminSession session, Guid? parentId, DateTimeOffset now)
    {
        var raw = Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        context.Add(new AdminRefreshCredential(Guid.NewGuid(), session.Id, parentId, Digest(raw), now, session.AbsoluteEnd));
        return raw;
    }

    private Task<CredentialOwner?> FindOwnerAsync(string digest, CancellationToken cancellationToken) =>
        (from credential in context.Set<AdminRefreshCredential>().AsNoTracking()
         join session in context.Set<AdminSession>().AsNoTracking() on credential.SessionId equals session.Id
         where credential.TokenHash == digest
         select new CredentialOwner(session.Id, session.AdminId)).SingleOrDefaultAsync(cancellationToken);

    private static async Task<T?> LockAsync<T>(IQueryable<T> query, CancellationToken cancellationToken) where T : class =>
        (await query.ToListAsync(cancellationToken)).SingleOrDefault();

    private static string Digest(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private sealed record LockedCredential(AdminIdentity Admin, AdminSession Session, AdminRefreshCredential Credential);

    private sealed record CredentialOwner(Guid SessionId, Guid AdminId);
}
