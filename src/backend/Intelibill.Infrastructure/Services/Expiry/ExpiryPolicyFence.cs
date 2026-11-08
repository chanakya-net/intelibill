using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Expiry;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Infrastructure.Services.Expiry;

internal sealed class ExpiryPolicyFence(ApplicationDbContext db) : IExpiryPolicyFence
{
    public async Task<ErrorOr<Success>> AcquireAsync(Guid shopId, bool exclusive, CancellationToken ct)
    {
        var transaction = db.Database.CurrentTransaction ?? throw new InvalidOperationException("Expiry policy fence requires a caller-owned transaction.");
        var previous = (await db.Database.SqlQuery<string>($"SELECT current_setting('lock_timeout') AS \"Value\"").ToListAsync(ct)).Single();
        // A savepoint lets even a timed-out lock restore its caller's timeout without aborting the entire transaction.
        const string savepoint = "expiry_policy_fence";
        await transaction.CreateSavepointAsync(savepoint, ct);
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {"5s"}, true)", ct);
            var rows = exclusive
                ? await db.Database.SqlQuery<Guid>($"SELECT shop_id AS \"Value\" FROM shop_expiry_states WHERE shop_id = {shopId} FOR UPDATE").ToListAsync(ct)
                : await db.Database.SqlQuery<Guid>($"SELECT shop_id AS \"Value\" FROM shop_expiry_states WHERE shop_id = {shopId} FOR SHARE").ToListAsync(ct);
            await db.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {previous}, true)", ct);
            await transaction.ReleaseSavepointAsync(savepoint, ct);
            return rows.Count == 1 ? Result.Success : Errors.Expiry.HistoryUnknown;
        }
        catch (Exception ex) when ((ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            await transaction.RollbackToSavepointAsync(savepoint, ct);
            await transaction.ReleaseSavepointAsync(savepoint, ct);
            return Errors.Expiry.PolicyBusy;
        }
    }
}
