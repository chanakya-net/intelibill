using System.Text.Json;
using Intelibill.Licensing.Infrastructure.Billing;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;
using static Intelibill.Shared.Contracts.V1.CoverageTransferApprovalsV1;

namespace Intelibill.Licensing.Infrastructure.Entitlements;

internal sealed class CoverageTransferAcknowledgements(LicensingDbContext db, EntitlementPublisher publisher,
    CoverageTransferStore store, TimeProvider transportClock)
{
    internal async Task<bool> ReceiveAsync(TransferReceiptV1 r, CancellationToken ct)
    {
        var message = await db.Database.SqlQuery<Message>($"""
            SELECT m.payload,m.digest FROM public.entitlement_messages m WHERE m.event_id={r.EventId}
                AND m.account_id={r.SourceAccountId} AND m.shop_id={r.ShopId} AND m.kind={r.Phase}
            """).SingleOrDefaultAsync(ct);
        if (message is null || message.Digest != r.CommandDigest) return false;
        var c = JsonSerializer.Deserialize<TransferCommandV1>(message.Payload, EntitlementSnapshotsV1.Json)!;
        if (c.TransferId != r.TransferId || c.DestinationAccountId != r.DestinationAccountId) return false;
        var status = await StateAsync(c.TransferId, ct);
        if ((r.Phase, status) is not ((Prepare, null) or (Consume, "Consuming") or (Abort, "NeedsReview"))) return false;
        var state = (r.Phase, r.Outcome) switch
        {
            (Prepare, "Prepared") => "Scheduled",
            (Consume, "Consumed") => "PendingActivation",
            (Abort, "Aborted") or (_, "Refused") => "NeedsReview",
            _ => null,
        };
        if (state is null) return false;
        await db.Database.ExecuteSqlAsync($"""
            UPDATE public.entitlement_outbox SET status='Applied',lease_token=NULL,lease_expires_at=NULL,last_failure=NULL
            WHERE event_id={r.EventId} AND account_id={r.SourceAccountId}
            """, ct);
        if (r.Phase == Abort) return true;
        var reason = r.Reason;
        if (state == "PendingActivation")
        {
            var decision = await ReadDecisionAsync(c.TransferId, ct);
            reason = await store.ApplicationBlockerAsync(c, decision.Consents, decision.DecidedAt, ct);
            if (reason is not null)
            {
                await EnqueueAsync(c with { EventId = Guid.NewGuid(), Phase = Abort }, ct);
                state = "NeedsReview";
            }
        }
        await ProgressAsync(c, state, reason, r.EventId, ct);
        if (state == "PendingActivation") await PublishSuccessorAsync(c, ct);
        return true;
    }

    internal async Task QueueDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        var due = await db.Database.SqlQuery<Due>($"SELECT * FROM public.due_transfer_applications({now})").ToListAsync(ct);
        foreach (var row in due)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await EnterPairAsync(db, row.SourceAccountId, row.DestinationAccountId, ct);
            if (await StateAsync(row.TransferId, ct) != "Scheduled") continue;
            var saved = await ReadDecisionAsync(row.TransferId, ct);
            var prepared = JsonSerializer.Deserialize<TransferCommandV1>(saved.Payload, EntitlementSnapshotsV1.Json)!;
            var reason = await store.ApplicationBlockerAsync(prepared, saved.Consents, saved.DecidedAt, ct);
            var cmd = prepared with { EventId = Guid.NewGuid(), Phase = reason is null ? Consume : Abort, PrepareDigest = saved.Digest };
            await EnqueueAsync(cmd, ct);
            await ProgressAsync(cmd, reason is null ? "Consuming" : "NeedsReview", reason, cmd.EventId, ct);
            await tx.CommitAsync(ct);
        }
    }

    private async Task EnqueueAsync(TransferCommandV1 cmd, CancellationToken ct)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(cmd, EntitlementSnapshotsV1.Json);
        await new LeasedHttpOutbox(db).AppendAsync(cmd.EventId, cmd.SourceAccountId, cmd.ShopId, cmd.Phase, body,
            SignedServiceAssertion.Digest(body), EntitlementSnapshotsV1.Instant(transportClock), ct);
    }

    private Task<Saved> ReadDecisionAsync(Guid transfer, CancellationToken ct) =>
        db.Database.SqlQuery<Saved>($"""
                SELECT m.payload,m.digest,a.consents,a.decided_at FROM public.coverage_transfer_approvals a
                JOIN public.entitlement_messages m ON m.event_id=a.prepare_event WHERE a.transfer_id={transfer}
                """).SingleAsync(ct);

    internal static async Task EnterPairAsync(LicensingDbContext db, Guid source, Guid destination, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'", ct);
        foreach (var account in new[] {source, destination}.Order()) await BillingAccountLock.TakeAsync(db, account, ct);
        await db.Database.ExecuteSqlAsync($"SELECT set_config('app.billing_account_id',{source.ToString("D")},true)", ct);
    }

    private Task<string?> StateAsync(Guid transfer,CancellationToken ct) => db.Database.SqlQuery<string?>($"""
        SELECT state AS "Value" FROM public.coverage_transfer_progress WHERE transfer_id={transfer}
        ORDER BY sequence DESC LIMIT 1
        """).SingleOrDefaultAsync(ct);

    private Task<int> ProgressAsync(TransferCommandV1 c, string state, string? reason, Guid id, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.coverage_transfer_progress(transfer_id,source_account_id,sequence,state,event_id,reason,recorded_at)
            SELECT {c.TransferId},{c.SourceAccountId},coalesce(max(sequence),0)+1,{state},{id},{reason}::text,{publisher.Now}
            FROM public.coverage_transfer_progress WHERE transfer_id={c.TransferId}
            """, ct);

    private async Task PublishSuccessorAsync(TransferCommandV1 c, CancellationToken ct)
    {
        var added = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.coverage_transfer_bindings(transfer_id,predecessor_id,source_account_id,destination_account_id,shop_id,
                shop_name,source_term_id,slot_origin_shop_id,member_allowance,owner_id,membership_id,authority_revision,cutover_at,paid_through_at)
            SELECT p.transfer_id,p.source_affiliation_id,p.source_account_id,p.destination_account_id,p.shop_id,p.shop_name,p.source_term_id,
                r.slot_origin_shop_id,{c.MemberAllowance},{c.ShopOwner},{c.MembershipId},{c.AuthorityRevision},{c.CutoverAt},{c.PaidThroughAt}
            FROM public.coverage_transfer_proposals p CROSS JOIN LATERAL public.pre_transfer_paid_slot_intervals(p.source_account_id,p.source_term_id) r
            WHERE p.transfer_id={c.TransferId} AND r.shop_id=p.shop_id AND r.affiliation_request_id=p.source_affiliation_id AND r.is_final_holder
            """, ct);
        if (added != 1) throw new InvalidOperationException("Transfer source slot no longer matches its reservation.");
        await db.Database.ExecuteSqlAsync($"""
            UPDATE public.coverage_transfer_proposals SET state='Retired', retired_reason='AllocationCommitted', retired_at={publisher.Now}
            WHERE transfer_id={c.TransferId} AND state='Open'
            """, ct);
        await db.Database.ExecuteSqlAsync($"SELECT set_config('app.billing_account_id',{c.DestinationAccountId.ToString("D")},true)", ct);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.shop_affiliation_requests(request_id,account_id,account_name,shop_id,requester_id,requester_revision,
                operation_key,created_at,expires_at)
            SELECT {c.TransferId},{c.DestinationAccountId},consent.account_name,{c.ShopId},consent.actor_id,
                consent.authority_revision,{c.EventId},consent.recorded_at,consent.recorded_at + interval '1 day'
            FROM public.coverage_transfer_consents consent WHERE consent.transfer_id={c.TransferId}
                AND consent.purpose='DestinationBillingOwner' AND consent.kind='Accepted' ORDER BY generation DESC LIMIT 1
            """, ct);
        await publisher.PublishTransferAsync(c, ct);
    }

    private sealed record Message(byte[] Payload, string Digest);
    private sealed record Saved(byte[] Payload, string Digest, Guid[] Consents, DateTimeOffset DecidedAt);
    private sealed record Due(Guid SourceAccountId, Guid DestinationAccountId, Guid TransferId);
}
