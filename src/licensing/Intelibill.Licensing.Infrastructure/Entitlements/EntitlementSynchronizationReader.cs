using System.Text.Json;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Entitlements;

/// <summary>Reads retained confirmation and current delivery state in one account-scoped PostgreSQL statement.</summary>
internal sealed class EntitlementSynchronizationReader(LicensingDbContext db, LicensingEntitlementTransport transport)
    : IEntitlementSynchronizationReader
{
    public async Task<IReadOnlyDictionary<Guid, EntitlementSynchronizationV1>> ReadAsync(Guid account, Guid[] shops,
        CancellationToken ct)
    {
        if (shops.Length == 0 || transport.Origin is null)
            return new Dictionary<Guid, EntitlementSynchronizationV1>();

        var rows = await db.Database.SqlQuery<DeliveryRow>($"""
            SELECT a.shop_id, saved.account_revision, saved.shop_revision, m.payload AS snapshot,
                o.status, o.last_failure, ack.payload AS acknowledgement, confirmed.payload AS confirmation
            FROM public.current_shop_affiliations a
            JOIN LATERAL (
                SELECT s.event_id, s.account_revision, s.shop_revision
                FROM public.entitlement_snapshots s
                WHERE s.account_id = a.account_id AND s.shop_id = a.shop_id AND s.affiliation_request_id = a.request_id
                ORDER BY s.shop_revision DESC LIMIT 1
            ) saved ON true
            JOIN public.entitlement_messages m ON m.event_id = saved.event_id
            JOIN public.entitlement_outbox o ON o.event_id = saved.event_id
            LEFT JOIN public.entitlement_merchant_inbox ack
                ON ack.event_id = saved.event_id AND ack.account_id = a.account_id AND ack.kind = 'Acknowledgement'
            LEFT JOIN LATERAL (
                SELECT i.payload FROM public.entitlement_snapshots s
                JOIN public.entitlement_outbox prior ON prior.event_id = s.event_id AND prior.status = 'Applied'
                JOIN public.entitlement_merchant_inbox i
                    ON i.event_id = s.event_id AND i.account_id = s.account_id AND i.kind = 'Acknowledgement'
                WHERE s.account_id = a.account_id AND s.shop_id = a.shop_id AND s.affiliation_request_id = a.request_id
                ORDER BY s.shop_revision DESC LIMIT 1
            ) confirmed ON true
            WHERE a.account_id = {account} AND a.shop_id = ANY({shops})
            """).ToListAsync(ct);
        return rows.ToDictionary(row => row.ShopId, Project);
    }

    private static EntitlementSynchronizationV1 Project(DeliveryRow row)
    {
        var snapshot = JsonSerializer.Deserialize<EntitlementSnapshotV1>(row.Snapshot, EntitlementSnapshotsV1.Json)!;
        var current = ReadReceipt(row.Acknowledgement);
        var confirmed = row.Status == "Applied"
            ? current : ReadReceipt(row.Confirmation);
        if (confirmed is not { Outcome: "Applied", Disposition: "Applied" or "Superseded",
                AccountRevision: > 0, ShopRevision: > 0, AppliedAccountRevision: > 0, AppliedShopRevision: > 0 } ||
            !EntitlementSnapshotsV1.IsInstant(confirmed.AppliedAt) ||
            confirmed.AppliedAccountRevision < confirmed.AccountRevision || confirmed.AppliedShopRevision < confirmed.ShopRevision)
            confirmed = null;

        var (state, delivery, attention) = row.Status switch
        {
            "Applied" when confirmed?.AppliedAccountRevision >= row.AccountRevision &&
                confirmed.AppliedShopRevision >= row.ShopRevision => ("Applied", "Acknowledged", null),
            "NeedsReview" when current is { Outcome: "NeedsReview" } &&
                SubscriptionStatusV1.ReviewDispositions.Contains(current.Disposition) => ("NeedsReview", "Acknowledged", current.Disposition),
            "Applied" or "NeedsReview" => ("NeedsReview", "Acknowledged", "InvalidAcknowledgement"),
            "DeadLetter" => ("NeedsReview", "DeadLetter", row.LastFailure ?? "DeliveryUnconfirmed"),
            "AwaitingAcknowledgement" => ("Pending", "AwaitingAcknowledgement", null),
            "Leased" when row.LastFailure == "AcknowledgementMissing" => ("Pending", "AwaitingAcknowledgement", null),
            "Pending" or "Leased" when row.LastFailure is null => ("Pending", "Queued", null),
            _ => ("Pending", "Retrying", (string?)null),
        };
        return new(state, delivery, row.AccountRevision, row.ShopRevision, snapshot.Shop!.UpstreamVerifiedAt,
            confirmed?.AppliedAccountRevision, confirmed?.AppliedShopRevision, confirmed?.AppliedAt, attention);
    }

    private static EntitlementAcknowledgementV1? ReadReceipt(byte[]? payload)
    {
        if (payload is null) return null;
        try { return JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(payload, EntitlementSnapshotsV1.Json); }
        catch (JsonException) { return null; }
    }

    private sealed record DeliveryRow(Guid ShopId, long AccountRevision, long ShopRevision, byte[] Snapshot,
        string Status, string? LastFailure, byte[]? Acknowledgement, byte[]? Confirmation);
}
