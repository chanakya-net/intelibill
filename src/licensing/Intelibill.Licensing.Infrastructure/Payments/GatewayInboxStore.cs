using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed class GatewayInboxStore(LicensingDbContext db) : IGatewayInboxStore
{
    public Task<bool> ReceiveAsync(VerifiedGatewayEvent parsed, byte[] hash, byte[] evidence, DateTimeOffset now,
        CancellationToken ct) => parsed.Refund is { } refund
        ? db.Database.SqlQuery<bool>($"""
            SELECT public.receive_refund_hint({Guid.NewGuid()}, {parsed.Pin.ProviderId}, {parsed.Pin.ProviderAccountId},
                {parsed.Pin.Environment}, {parsed.Pin.ConfigurationVersion}, {parsed.ProviderEventId}, {refund.PaymentReference},
                {refund.RefundReference}, {refund.Receipt}, {hash}, {evidence}, {now}) AS "Value"
            """).SingleAsync(ct)
        : db.Database.SqlQuery<bool>($"""
        SELECT public.receive_gateway_inbox_event({Guid.NewGuid()}, {parsed.Pin.ProviderId}, {parsed.Pin.ProviderAccountId},
            {parsed.Pin.Environment}, {parsed.Pin.ConfigurationVersion}, {parsed.ProviderEventId}, {parsed.Kind},
            {parsed.Payment?.ProviderOrderReference}, {parsed.Payment?.ProviderPaymentReference}, {hash}, {evidence}, {now}) AS "Value"
        """).SingleAsync(ct);

    public async Task<GatewayInboxLease?> ClaimAsync(DateTimeOffset now, CancellationToken ct)
    {
        var token = Guid.NewGuid();
        var rows = await db.Database.SqlQuery<Claimed>($"""
            SELECT * FROM public.claim_gateway_inbox_event({now}, {token})
            """).ToListAsync(ct);
        return rows is [var row] ? new(row.InboxId, token, row.Kind, row.OrderReference, row.PaymentReference,
            row.AccountId, row.AttemptId, row.Candidates, row.Attempts) : null;
    }

    public Task<bool> LockAsync(GatewayInboxLease lease, DateTimeOffset now, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT public.lock_gateway_inbox_event({lease.InboxId}, {lease.Token}, {now}) AS "Value"
            """).SingleAsync(ct);

    public Task<bool> FinishAsync(GatewayInboxLease lease, GatewayInboxResolution resolution, DateTimeOffset now,
        CancellationToken ct) => db.Database.SqlQuery<bool>($"""
        SELECT public.finish_gateway_inbox_event({lease.InboxId}, {lease.Token}, {now}, {resolution.Outcome?.ToString()},
            {resolution.Failure?.ToString()}, {resolution.DeadLetter}) AS "Value"
        """).SingleAsync(ct);

    private sealed record Claimed(Guid InboxId, string Kind, string? OrderReference, string? PaymentReference,
        Guid? AccountId, Guid? AttemptId, int Candidates, int Attempts);
}
