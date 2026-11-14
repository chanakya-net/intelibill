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
internal sealed class AdminRefreshCredentialService(LicensingDbContext context, TimeProvider clock) : IAdminRefreshCredentials
{
    private const string Prefix = "r1_";
    private const int EncodedLength = 46;

    /// <summary>Stages the first credential of a new session in the caller's transaction.</summary>
    internal string IssueRoot(AdminSession session) => Issue(session, parentId: null, session.CreatedAt);

    public async Task<ErrorOr<AdminSessionGrant>> RotateAsync(string? presented, CancellationToken cancellationToken)
    {
        if (presented is not { Length: EncodedLength } || !presented.StartsWith(Prefix, StringComparison.Ordinal))
            return AdminRefreshErrors.SessionEnded;
        var digest = Digest(presented);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", cancellationToken);
        var owner = await FindOwnerAsync(digest, cancellationToken);
        if (owner is null) return AdminRefreshErrors.SessionEnded;

        var admin = await LockAsync(context.Set<AdminIdentity>().FromSql($"""
            SELECT *, xmin FROM public.admin_identities WHERE id = {owner.AdminId} FOR UPDATE
            """), cancellationToken);
        var session = await LockAsync(context.Set<AdminSession>().FromSql($"""
            SELECT * FROM public.admin_sessions WHERE id = {owner.SessionId} FOR UPDATE
            """), cancellationToken);
        var credential = await LockAsync(context.Set<AdminRefreshCredential>().FromSql($"""
            SELECT * FROM public.admin_refresh_credentials WHERE token_hash = {digest} FOR UPDATE
            """), cancellationToken);
        if (admin is null || session is null || credential is null) return AdminRefreshErrors.SessionEnded;

        var result = Decide(admin, session, credential, clock.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

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
        session.Revoke(now);
        context.Add(new ProviderAuditEvent(Guid.NewGuid(), BootstrapAuditAction.SessionReuseRevoked, now, session.AdminId));
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

    private sealed record CredentialOwner(Guid SessionId, Guid AdminId);
}
