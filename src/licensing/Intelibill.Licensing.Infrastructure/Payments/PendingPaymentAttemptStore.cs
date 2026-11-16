using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed class PendingPaymentAttemptStore(LicensingDbContext db) : IPendingPaymentAttemptStore
{
    public async Task<PendingPaymentLease?> ClaimAsync(DateTimeOffset now, CancellationToken ct)
    {
        var token = Guid.NewGuid();
        var rows = await db.Database.SqlQuery<Claimed>($"""
            SELECT account_id, attempt_id FROM public.claim_pending_payment({now}, {token})
            """).ToListAsync(ct);
        return rows is [var row] ? new(row.AccountId, row.AttemptId, token) : null;
    }

    public Task<bool> LockAsync(PendingPaymentLease lease, DateTimeOffset now, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT public.lock_pending_payment({lease.Token}, {now}) AS "Value"
            """).SingleAsync(ct);

    public Task CompleteAsync(CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT public.complete_pending_payment()", ct);

    public Task FinishAsync(PendingPaymentLease lease, DateTimeOffset now, bool terminal, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT public.finish_pending_payment({lease.Token}, {now}, {terminal})", ct);

    private sealed record Claimed(Guid AccountId, Guid AttemptId);
}
