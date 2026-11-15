using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Audit;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities;

internal sealed class AdminCapabilitiesStore(LicensingDbContext context, IAdminRecoveryStore transactions,
    AuditEvidenceFactory audit) : IAdminCapabilitiesStore
{
    public async Task<ErrorOr<T>> InTransactionAsync<T>(
        Func<CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken token)
    {
        try
        {
            return await transactions.InTransactionAsync(operation, token);
        }
        catch (Exception exception) when ((exception as PostgresException
            ?? exception.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            return AdminCapabilityErrors.Busy;
        }
    }

    public Task LockOwnerFenceAsync(CancellationToken token) => AdminAuthorityLocks.LockOwnerFenceAsync(context, token);

    public async Task<(AdminIdentity? Actor, AdminIdentity? Target)> LockIdentitiesAsync(Guid actorId, Guid targetId,
        CancellationToken token)
    {
        var rows = await context.Set<AdminIdentity>().FromSql($"""
            SELECT *, xmin FROM public.admin_identities WHERE id IN ({actorId}, {targetId}) ORDER BY id FOR UPDATE
            """).ToListAsync(token);
        return (rows.SingleOrDefault(row => row.Id == actorId), rows.SingleOrDefault(row => row.Id == targetId));
    }

    public Task<AdminSession?> LockSessionAsync(AdminSessionCaller caller, CancellationToken token) =>
        AdminAuthorityLocks.LockSessionAsync(context, caller, token);

    public Task<bool> HasOtherRecoverableOwnerAsync(Guid adminId, CancellationToken token) =>
        AdminAuthorityLocks.RecoverableOwners(context).AnyAsync(owner => owner.Id != adminId, token);

    public async Task<IReadOnlyList<AdminIdentity>> ListAsync(Guid? after, int take, CancellationToken token)
    {
        var query = context.Set<AdminIdentity>().AsNoTracking();
        if (after is { } id) query = query.Where(admin => admin.Id.CompareTo(id) > 0);
        return await query.OrderBy(admin => admin.Id).Take(take).ToListAsync(token);
    }

    public async Task<IReadOnlyList<AdminSession>> LockTargetSessionsAsync(Guid adminId, CancellationToken token) =>
        await AdminAuthorityLocks.LockOpenSessionsAsync(context, adminId, token);

    public async Task RevokeSessionsAsync(IReadOnlyList<AdminSession> sessions, DateTimeOffset now, CancellationToken token)
    {
        foreach (var session in sessions)
            await AdminSessionRevocation.StageAsync(context, audit, session, now, BootstrapAuditAction.DeviceSessionRevoked,
                AuditReason.AdminAuthorityChanged, token, AuditActor.System);
    }

    public Task FlushAsync(CancellationToken token) => context.SaveChangesAsync(token);

    public void Add(ProviderAuditEvent auditEvent) => context.Add(auditEvent);
}
