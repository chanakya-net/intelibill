using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities;

internal sealed class AdminInvitationStore(LicensingDbContext context) : IAdminInvitationStore
{
    public Task<string?> FindOwnerOriginAsync(Guid adminId, CancellationToken cancellationToken) =>
        (from state in context.Set<InitialAdminBootstrap>()
         join owner in context.Set<AdminIdentity>() on state.OwnerId equals owner.Id
         where owner.Id == adminId && state.ConsumedAt != null && owner.MfaEnrolledAt != null
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

    public async Task<IReadOnlyList<Guid>> SupersedeOpenAsync(string email, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var open = context.Set<AdminInvitation>()
            .Where(invitation => invitation.Email == email && invitation.AcceptedAt == null && invitation.RevokedAt == null);
        var ids = await open.Select(invitation => invitation.Id).ToListAsync(cancellationToken);
        await open.ExecuteUpdateAsync(set => set.SetProperty(invitation => invitation.RevokedAt, now), cancellationToken);
        return ids;
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
            await context.Set<AdminInvitation>()
                .Where(invitation => invitation.Id == invitationId && invitation.AcceptedAt == null && invitation.RevokedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(invitation => invitation.RevokedAt, now), token);
            context.Add(new ProviderAuditEvent(Guid.NewGuid(), BootstrapAuditAction.InvitationUndelivered, now, actorId, invitationId));
            return true;
        }, cancellationToken);
    }

    public void Add(AdminInvitation invitation) => context.Add(invitation);
    public void Add(AdminIdentity identity) => context.Add(identity);
    public void Add(BootstrapEnrollmentCredential credential) => context.Add(credential);
    public void Add(ProviderAuditEvent auditEvent) => context.Add(auditEvent);
}
