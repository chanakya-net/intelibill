using Intelibill.Licensing.Application.Payments;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed partial class PaymentStore
{
    /// <summary>Caller holds the account lock and attempt scope. Append transitions, never replace prior evidence.</summary>
    public Task RecordRoutingAsync(StoredPaymentAttempt attempt, string? blocker, DateTimeOffset at, CancellationToken ct)
    {
        var kind = blocker is null ? "Restored" : "Unavailable";
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.payment_routing_events (account_id, attempt_id, kind, reason, recorded_at)
            SELECT {attempt.AccountId}, {attempt.AttemptId}, {kind}, {blocker}, {at}
            WHERE coalesce((SELECT kind FROM public.payment_routing_events WHERE attempt_id = {attempt.AttemptId}
                ORDER BY sequence DESC LIMIT 1), 'Restored') <> {kind}
            """, ct);
    }
}
