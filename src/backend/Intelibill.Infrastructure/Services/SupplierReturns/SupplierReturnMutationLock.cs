using Intelibill.Application.Features.SupplierReturns.Services;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Infrastructure.Services.SupplierReturns;
internal sealed class SupplierReturnMutationLock(ApplicationDbContext context) : ISupplierReturnMutationLock
{
    public Task<RowLockOutcome> LockAsync(Guid shopId,Guid supplierId,IReadOnlyCollection<Guid> ids,CancellationToken cancellationToken) => WithTimeoutAsync(async () =>
    {
        var supplier=await context.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM suppliers WHERE id={supplierId} AND shop_id={shopId} FOR NO KEY UPDATE").ToListAsync(cancellationToken);
        if(supplier.Count==0) return RowLockOutcome.NotFound;
        foreach(var id in ids.Distinct().Order())
            _=await context.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM purchase_receipt_values WHERE id={id} AND shop_id={shopId} FOR NO KEY UPDATE").ToListAsync(cancellationToken);
        return RowLockOutcome.Acquired;
    },cancellationToken);
    public Task<RowLockOutcome> LockReturnAsync(Guid shopId,Guid id,CancellationToken cancellationToken) => WithTimeoutAsync(async () =>
    {
        var rows=await context.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM supplier_returns WHERE id={id} AND shop_id={shopId} FOR NO KEY UPDATE").ToListAsync(cancellationToken);
        return rows.Count==0?RowLockOutcome.NotFound:RowLockOutcome.Acquired;
    },cancellationToken);
    private async Task<RowLockOutcome> WithTimeoutAsync(Func<Task<RowLockOutcome>> action,CancellationToken ct)
    {
        if(!context.Database.IsNpgsql()) return RowLockOutcome.Acquired;
        if(context.Database.CurrentTransaction is null) throw new InvalidOperationException("Supplier return locks require a transaction.");
        var prior=await context.Database.SqlQuery<string>($"SELECT current_setting('lock_timeout') AS \"Value\"").SingleAsync(ct);
        await context.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {"5s"}, true)",ct);
        try { var result=await action(); await context.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {prior}, true)",ct); return result; }
        catch(Exception ex) when ((ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState==PostgresErrorCodes.LockNotAvailable) { return RowLockOutcome.TimedOut; }
    }
}
