using Intelibill.Shared.Web.Persistence;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Licensing;

internal sealed class EntitlementAcknowledgementMetrics(ApplicationDbContext db, Meter meter, TimeProvider clock)
{
    private readonly Histogram<double> _lag = meter.CreateHistogram<double>(
        "intelibill.licensing.entitlement.acknowledgement_lag", "s",
        "Time from upstream shop verification to the first observed acceptance of the applied merchant receipt.");

    public async Task RecordAsync(LeasedHttpMessage message, CancellationToken ct)
    {
        var confirmedAt = EntitlementSnapshotsV1.Instant(clock);
        if (JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(message.Payload, EntitlementSnapshotsV1.Json)
                is not { SchemaVersion: 1, Outcome: "Applied", Disposition: "Applied" or "Superseded" } ack ||
            ack.EventId != message.EventId || ack.AccountRevision is not > 0 || ack.ShopRevision is not > 0 ||
            !(ack.AppliedAccountRevision >= ack.AccountRevision && ack.AppliedShopRevision >= ack.ShopRevision) ||
            ack.AppliedAt > confirmedAt)
            return;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"""
            SELECT set_config('app.active_shop_id', {ack.ShopId.ToString("D")}, true),
                set_config('lock_timeout', {DatabaseLockTimeouts.Value(db, LockWait.Brief)}, true)
            """, ct);
        var verifiedTimes = await ConfirmAsync(ack, message.Payload, confirmedAt, ct);
        await transaction.CommitAsync(ct);
        foreach (var verifiedAt in verifiedTimes)
            _lag.Record((confirmedAt - verifiedAt).TotalSeconds);
    }

    private Task<List<DateTimeOffset>> ConfirmAsync(EntitlementAcknowledgementV1 ack, byte[] payload,
        DateTimeOffset confirmedAt, CancellationToken ct) =>
        db.Database.SqlQuery<DateTimeOffset>($"""
            WITH confirmed AS (
                UPDATE public.entitlement_outbox o SET ack_confirmed_at = {confirmedAt}
                FROM public.entitlement_messages m
                JOIN public.entitlement_snapshot_inbox i USING (event_id, account_id, shop_id)
                JOIN public.license_grant_evidence e USING (event_id, account_id, shop_id)
                WHERE o.event_id = m.event_id AND o.event_id = {ack.EventId}
                    AND o.account_id = {ack.AccountId} AND o.shop_id = {ack.ShopId}
                    AND o.ack_confirmed_at IS NULL AND m.kind = 'Acknowledgement'
                    AND m.payload = {payload} AND i.acknowledgement = {payload} AND i.digest = {ack.Digest}
                    AND e.digest = {ack.Digest} AND e.affiliation_request_id = {ack.AffiliationRequestId}
                    AND e.account_revision = {ack.AccountRevision} AND e.shop_revision = {ack.ShopRevision}
                    AND e.applied_at = {ack.AppliedAt} AND e.shop_upstream_verified_at <= e.applied_at
                RETURNING e.shop_upstream_verified_at
            )
            SELECT shop_upstream_verified_at AS "Value" FROM confirmed
            """).ToListAsync(ct);
}
