using Intelibill.Licensing.Application.Audit;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities;

internal sealed class AdminInvitationStore(LicensingDbContext context, AuditEvidenceFactory audit) : IAdminInvitationStore
{
    public Task<string?> FindOwnerOriginAsync(Guid adminId, CancellationToken cancellationToken) =>
        (from state in context.Set<InitialAdminBootstrap>()
         join owner in context.Set<AdminIdentity>() on state.OwnerId equals owner.Id
         where owner.Id == adminId && state.ConsumedAt != null && owner.MfaEnrolledAt != null && owner.DeactivatedAt == null
         select state.AdminOrigin).SingleOrDefaultAsync(cancellationToken);

    public Task<string?> FindProviderOriginAsync(CancellationToken cancellationToken) =>
        context.Set<InitialAdminBootstrap>().Where(state => state.ConsumedAt != null)
            .Select(state => state.AdminOrigin).SingleOrDefaultAsync(cancellationToken);

    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", cancellationToken);
        var result = await operation(cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public Task LockEmailAsync(string email, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended('admin-email:' || {email}, 0))", cancellationToken);

    public Task<bool> AdminExistsAsync(string email, CancellationToken cancellationToken) =>
        context.Set<AdminIdentity>().AnyAsync(admin => admin.Email == email, cancellationToken);

    public async Task<IReadOnlyList<InvitationTransition>> SupersedeOpenAsync(string email, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rows = await context.Set<AdminInvitation>().FromSql($"""
            SELECT *, xmin FROM public.admin_invitations
            WHERE email = {email} AND accepted_at IS NULL AND revoked_at IS NULL FOR UPDATE
            """).ToListAsync(cancellationToken);
        return await RetireAsync(rows, now, cancellationToken);
    }

    public Task FlushAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    private async Task<InvitationTransition[]> RetireAsync(
        List<AdminInvitation> rows, DateTimeOffset now, CancellationToken token)
    {
        if (rows.Count == 0) return [];
        var before = rows.Select(row => row.AuditReferenceAt(now)).ToArray();
        foreach (var row in rows) row.Retire(now);
        await FlushAsync(token);
        return rows.Select((row, index) => new InvitationTransition(before[index], row.AuditReferenceAt(now))).ToArray();
    }

    public Task<string?> FindInvitedEmailAsync(string digest, CancellationToken cancellationToken) =>
        context.Set<AdminInvitation>().Where(invitation => invitation.TokenDigest == digest)
            .Select(invitation => invitation.Email).SingleOrDefaultAsync(cancellationToken);

    public async Task<AdminInvitation?> LockInvitationAsync(string digest, CancellationToken cancellationToken)
    {
        var rows = await context.Set<AdminInvitation>()
            .FromSql($"SELECT *, xmin FROM public.admin_invitations WHERE token_digest = {digest} FOR UPDATE")
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public async Task RetireUndeliveredAsync(Guid invitationId, Guid actorId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await InTransactionAsync(async token =>
        {
            var local = context.Set<AdminInvitation>().Local.FindEntry(invitationId);
            if (local is not null) local.State = EntityState.Detached;
            var rows = await context.Set<AdminInvitation>().FromSql($"""
                SELECT *, xmin FROM public.admin_invitations
                WHERE id = {invitationId} AND accepted_at IS NULL AND revoked_at IS NULL FOR UPDATE
                """).ToListAsync(token);
            var change = (await RetireAsync(rows, now, token)).SingleOrDefault();
            context.Add(audit.Create(BootstrapAuditAction.InvitationUndelivered, now, AuditActor.Administrator,
                AuditReason.InvitationDeliveryFailed, actorId, invitationId, change?.Before, change?.After));
            return true;
        }, cancellationToken);
    }

    public void Add(AdminInvitation invitation) => context.Add(invitation);
    public void Add(AdminIdentity identity) => context.Add(identity);
    public void Add(BootstrapEnrollmentCredential credential) => context.Add(credential);
    public void Add(ProviderAuditEvent auditEvent) => context.Add(auditEvent);
}
