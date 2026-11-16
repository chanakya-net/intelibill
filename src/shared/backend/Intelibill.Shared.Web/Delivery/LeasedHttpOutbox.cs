using Microsoft.EntityFrameworkCore;

namespace Intelibill.Shared.Web.Delivery;

public sealed record LeasedHttpMessage(Guid EventId, string Kind, byte[] Payload, int Attempts);

public sealed class LeasedHttpOutbox(DbContext db)
{
    public async Task<LeasedHttpMessage?> ClaimAsync(DateTimeOffset now, Guid lease, CancellationToken ct)
    {
        // Materialize the volatile SQL function once. No database transaction spans HTTP.
        var rows = await db.Database.SqlQuery<LeasedHttpMessage>(
            $"SELECT * FROM public.claim_entitlement_delivery({now}, {lease})").ToListAsync(ct);
        return rows.SingleOrDefault();
    }

    public Task FinishAsync(Guid id, Guid lease, DateTimeOffset now, bool success, bool waitForAck, string? failure,
        CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT public.finish_entitlement_delivery({id}, {lease}, {now}, {success}, {waitForAck}, {failure})
            """, ct);

    public async Task AppendAsync(Guid id, Guid account, Guid shop, string kind, byte[] payload, string digest,
        DateTimeOffset now, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.entitlement_messages (event_id, account_id, shop_id, kind, payload, digest)
            VALUES ({id}, {account}, {shop}, {kind}, {payload}, {digest})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.entitlement_outbox (event_id, account_id, shop_id, next_attempt_at)
            VALUES ({id}, {account}, {shop}, {now})
            """, ct);
    }
}
