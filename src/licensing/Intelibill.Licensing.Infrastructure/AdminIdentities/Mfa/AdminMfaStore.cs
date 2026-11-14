using ErrorOr;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities.Mfa;

internal sealed class AdminMfaStore(LicensingDbContext context)
{
    internal async Task<ErrorOr<T>> WithAdminAsync<T>(Guid adminId,
        Func<AdminIdentity, Task<ErrorOr<T>>> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", cancellationToken);
        var admin = await LockAdminAsync(context, adminId, cancellationToken);
        if (admin is null) return AdminMfaEnrollmentService.Refused;

        var result = await operation(admin);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    // Resume and every MFA mutation serialize on this row before touching credentials.
    internal static async Task<AdminIdentity?> LockAdminAsync(
        LicensingDbContext context, Guid adminId, CancellationToken cancellationToken)
    {
        // A caller can verify more than once per scope. Discard any snapshot loaded by
        // the preceding transaction before reading under the new lock.
        var tracked = context.Set<AdminIdentity>().Local.FindEntry(adminId);
        if (tracked is not null) tracked.State = EntityState.Detached;
        var rows = await context.Set<AdminIdentity>()
            .FromSql($"SELECT *, xmin FROM public.admin_identities WHERE id = {adminId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    internal Task<AdminTotp?> ReadTotpAsync(Guid adminId, CancellationToken cancellationToken)
    {
        var tracked = context.Set<AdminTotp>().Local.FindEntry(adminId);
        if (tracked is not null) tracked.State = EntityState.Detached;
        return context.Set<AdminTotp>().SingleOrDefaultAsync(totp => totp.AdminId == adminId, cancellationToken);
    }

    internal Task<AdminRecoveryCode?> ReadRecoveryAsync(Guid adminId, string digest, CancellationToken cancellationToken) =>
        context.Set<AdminRecoveryCode>().SingleOrDefaultAsync(
            code => code.AdminId == adminId && code.Digest == digest && code.ConsumedAt == null, cancellationToken);

    internal void Add(AdminTotp totp) => context.Add(totp);
    internal void Add(AdminRecoveryCode code) => context.Add(code);
    internal void Audit(Guid adminId, BootstrapAuditAction action, DateTimeOffset now) =>
        context.Add(new ProviderAuditEvent(Guid.NewGuid(), action, now, adminId));
}
