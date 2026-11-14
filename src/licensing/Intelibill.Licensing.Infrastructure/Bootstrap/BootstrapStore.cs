using Intelibill.Licensing.Application.Bootstrap;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Licensing.Infrastructure.AdminIdentities.Mfa;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Bootstrap;

internal sealed class BootstrapStore(LicensingDbContext context) : IBootstrapStore
{
    public Task<InitialAdminBootstrap> ReadAsync(CancellationToken cancellationToken) =>
        context.Set<InitialAdminBootstrap>().AsNoTracking().SingleAsync(cancellationToken);

    public async Task<T> WithLockedBootstrapAsync<T>(
        Func<InitialAdminBootstrap, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", cancellationToken);
        var rows = await context.Set<InitialAdminBootstrap>()
            .FromSqlRaw("SELECT *, xmin FROM public.initial_admin_bootstrap WHERE id = 1 FOR UPDATE")
            .ToListAsync(cancellationToken);
        var result = await operation(rows.Single(), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<AdminIdentity?> LockPendingOwnerAsync(Guid id, CancellationToken cancellationToken)
    {
        var admin = await AdminMfaStore.LockAdminAsync(context, id, cancellationToken);
        return admin is { MfaEnrolledAt: null, DeactivatedAt: null } ? admin : null;
    }

    public Task<AdminIdentity?> FindPendingOwnerAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<AdminIdentity>().AsNoTracking()
            .SingleOrDefaultAsync(owner => owner.Id == id && owner.MfaEnrolledAt == null && owner.DeactivatedAt == null, cancellationToken);

    public Task<EnrollmentState?> FindEnrollmentAsync(string digest, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var query =
            from credential in context.Set<BootstrapEnrollmentCredential>()
            join owner in context.Set<AdminIdentity>() on credential.OwnerId equals owner.Id
            where credential.Digest == digest && credential.ExpiresAt > now && credential.RevokedAt == null
                && owner.MfaEnrolledAt == null && owner.DeactivatedAt == null
                // Only the bootstrapped owner or an administrator who accepted an invitation.
                && (context.Set<InitialAdminBootstrap>().Any(state => state.OwnerId == owner.Id && state.ConsumedAt != null)
                    || context.Set<AdminInvitation>().Any(invitation => invitation.AcceptedAdminId == owner.Id
                        && invitation.AcceptedAt != null))
            select new EnrollmentState(owner.Id, credential.ExpiresAt);
        return query.SingleOrDefaultAsync(cancellationToken);
    }

    public Task RevokeEnrollmentAsync(Guid ownerId, DateTimeOffset now, CancellationToken cancellationToken) =>
        context.Set<BootstrapEnrollmentCredential>()
            .Where(credential => credential.OwnerId == ownerId && credential.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(credential => credential.RevokedAt, now), cancellationToken);

    public void Add(AdminIdentity identity) => context.Add(identity);
    public void Add(BootstrapEnrollmentCredential credential) => context.Add(credential);
    public void Add(ProviderAuditEvent auditEvent) => context.Add(auditEvent);
}
