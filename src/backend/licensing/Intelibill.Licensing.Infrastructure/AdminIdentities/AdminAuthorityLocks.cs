using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities;

internal static class AdminAuthorityLocks
{
    // Shared by grant removal, disable and credential resets. The next statement observes the preceding
    // writer's committed state at READ COMMITTED, even when separate identities were changed.
    internal static Task LockOwnerFenceAsync(LicensingDbContext context, CancellationToken token) =>
        context.Database.ExecuteSqlRawAsync(
            "SELECT id FROM public.initial_admin_bootstrap WHERE id = 1 FOR UPDATE", token);

    internal static IQueryable<AdminIdentity> RecoverableOwners(LicensingDbContext context) =>
        context.Set<AdminIdentity>().Where(owner => owner.MfaEnrolledAt != null && owner.DeactivatedAt == null
            && (owner.Capabilities & AdminCapability.PlatformOwner) == AdminCapability.PlatformOwner);

    internal static async Task<AdminSession?> LockSessionAsync(LicensingDbContext context, AdminSessionCaller caller,
        CancellationToken token)
    {
        var rows = await context.Set<AdminSession>().FromSql($"""
            SELECT *, xmin FROM public.admin_sessions WHERE id = {caller.SessionId} AND admin_id = {caller.AdminId}
            FOR UPDATE
            """).ToListAsync(token);
        return rows.SingleOrDefault();
    }

    internal static Task<List<AdminSession>> LockOpenSessionsAsync(LicensingDbContext context, Guid adminId,
        CancellationToken token) => context.Set<AdminSession>().FromSql($"""
            SELECT *, xmin FROM public.admin_sessions WHERE admin_id = {adminId} AND revoked_at IS NULL
            ORDER BY id FOR UPDATE
            """).ToListAsync(token);
}
