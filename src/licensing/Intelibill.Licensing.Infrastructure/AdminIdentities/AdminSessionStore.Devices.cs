using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities;

internal sealed partial class AdminSessionStore
{
    public async Task<ErrorOr<AdminDeviceSessionPage>> ListDevicesAsync(AdminSessionCaller caller, int pageSize,
        AdminDeviceSessionSeek? seek, CancellationToken token)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", token);
        var scope = await LockDevicesAsync(caller, caller.SessionId, token);
        if (scope.Caller is null) return AdminRefreshErrors.SessionEnded;
        var rows = await ReadLiveDevicesAsync(caller, scope.Now, pageSize, seek, token);
        context.Add(audit.Create(BootstrapAuditAction.DeviceSessionsInspected, scope.Now,
            AuditActor.Administrator, AuditReason.DeviceSessionsInspected, caller.AdminId));
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        var last = rows.Count > pageSize ? rows[pageSize - 1] : null;
        return new AdminDeviceSessionPage(rows.Take(pageSize).ToArray(),
            last is null ? null : new AdminDeviceSessionSeek(last.CreatedAt, last.Id).ToString());
    }

    private Task<List<AdminDeviceSession>> ReadLiveDevicesAsync(AdminSessionCaller caller, DateTimeOffset now,
        int pageSize, AdminDeviceSessionSeek? seek, CancellationToken token)
    {
        var rows = context.Set<AdminSession>().AsNoTracking().Where(session =>
            session.AdminId == caller.AdminId && session.RevokedAt == null && session.MfaVerifiedAt <= now
            && session.CreatedAt > now - AdminSession.AbsoluteLifetime && session.LastSeenAt > now - AdminSession.IdleLifetime);
        if (seek is not null)
            rows = rows.Where(session => EF.Functions.LessThan(
                ValueTuple.Create(session.CreatedAt, session.Id), ValueTuple.Create(seek.CreatedAt, seek.Id)));
        return rows.OrderByDescending(session => session.CreatedAt).ThenByDescending(session => session.Id)
            .Take(pageSize + 1).Select(session => new AdminDeviceSession(session.Id, session.Id == caller.SessionId,
                session.CreatedAt, session.LastSeenAt, session.CreatedAt + AdminSession.AbsoluteLifetime)).ToListAsync(token);
    }

    public async Task<ErrorOr<Success>> RevokeDeviceAsync(AdminSessionCaller caller, Guid targetId, AuditReason reason,
        CancellationToken token)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", token);
        var scope = await LockDevicesAsync(caller, targetId, token);
        if (scope.Caller is null) return AdminRefreshErrors.SessionEnded;

        var outcome = await RevokeLockedDeviceAsync(caller, scope, reason, token);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return outcome;
    }

    public async Task SignOutDeviceAsync(AdminSessionCaller caller, CancellationToken token)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", token);
        var scope = await LockDevicesAsync(caller, caller.SessionId, token);
        if (scope.Caller is null) return;
        await AdminSessionRevocation.StageAsync(context, audit, scope.Caller, scope.Now,
            BootstrapAuditAction.DeviceSignedOut, AuditReason.SignedOut, token);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    private async Task<ErrorOr<Success>> RevokeLockedDeviceAsync(AdminSessionCaller caller, LockedDevices scope,
        AuditReason reason, CancellationToken token)
    {
        if (scope.Target is null)
            return Denied(caller, scope.Now, AuditReason.ScopeUnavailable, AdminDeviceSessions.TargetUnavailable);
        if (scope.Target.Id != caller.SessionId && !new ProviderActor(caller.AdminId, scope.Caller!.MfaVerifiedAt).HasRecentMfa(scope.Now))
            return Denied(caller, scope.Now, AuditReason.RecentMfaRequired, AdminDeviceSessions.RecentMfaRequired);
        await AdminSessionRevocation.StageAsync(context, audit, scope.Target, scope.Now,
            BootstrapAuditAction.DeviceSessionRevoked, reason, token);
        return Result.Success;
    }

    private Error Denied(AdminSessionCaller caller, DateTimeOffset now, AuditReason reason, Error error)
    {
        context.Add(audit.Create(BootstrapAuditAction.DeviceSessionDenied, now, AuditActor.Administrator,
            reason, caller.AdminId));
        return error;
    }

    private async Task<LockedDevices> LockDevicesAsync(AdminSessionCaller caller, Guid targetId, CancellationToken token)
    {
        var admins = await context.Set<AdminIdentity>().FromSql($"""
            SELECT *, xmin FROM public.admin_identities WHERE id = {caller.AdminId} FOR UPDATE
            """).ToListAsync(token);
        if (admins.SingleOrDefault() is not { DeactivatedAt: null, MfaEnrolledAt: not null })
            return new(null, null, clock.GetUtcNow());

        var devices = await context.Set<AdminSession>().FromSql($"""
            SELECT *, xmin FROM public.admin_sessions
            WHERE admin_id = {caller.AdminId} AND (id = {caller.SessionId} OR id = {targetId})
            ORDER BY id FOR UPDATE
            """).ToListAsync(token);
        var now = clock.GetUtcNow();
        var actor = devices.SingleOrDefault(row => row.Id == caller.SessionId);
        return new(now < caller.AccessExpiresAt && actor?.IsLiveAt(now) == true ? actor : null,
            devices.SingleOrDefault(row => row.Id == targetId), now);
    }

    private sealed record LockedDevices(AdminSession? Caller, AdminSession? Target, DateTimeOffset Now);
}
