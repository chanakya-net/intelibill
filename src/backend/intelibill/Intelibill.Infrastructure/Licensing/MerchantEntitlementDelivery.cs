using Intelibill.Application.Common.Licensing;
using System.Net;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Licensing;

[LicenceAction(LicenceAction.InternalService)]
internal sealed class MerchantEntitlementDelivery(ApplicationDbContext db, EntitlementProjectionStore projection,
    MerchantEntitlementTransport transport, LeasedHttpOutbox outbox, IHttpClientFactory clients, TimeProvider clock,
    EntitlementAcknowledgementMetrics acknowledgements)
    : ILeasedHttpDestination
{
    public bool Enabled => transport.Origin is not null;

    public async Task<string?> SendAsync(LeasedHttpMessage message, CancellationToken ct)
    {
        var path = message.Kind switch
        {
            "Acknowledgement" => EntitlementSnapshotsV1.AcknowledgementPath,
            CoverageReplacementsV1.Kind => CoverageReplacementsV1.ReceiptPath,
            CoverageReplacementExceptionsV1.ConsentKind => CoverageReplacementExceptionsV1.ConsentReceiptPath,
            CoverageReplacementExceptionsV1.ReceiptKind => CoverageReplacementExceptionsV1.DecisionReceiptPath,
            CoverageTransferApprovalsV1.Receipt => CoverageTransferApprovalsV1.ReceiptPath,
            CoverageTransfersV1.ConsentKind => CoverageTransfersV1.ConsentReceiptPath,
            CoverageSlotsV1.Kind => CoverageSlotsV1.ReceiptPath,
            CapacityReservationV1.Receipt => CapacityReservationV1.ReceiptPath,
            ReductionTransportV1.ReceiptKind => ReductionTransportV1.ReceiptPath,
            "Reconciliation" => EntitlementSnapshotsV1.ReconciliationPath,
            OfflineDeviceObservationsV1.Kind => OfflineDeviceObservationsV1.Path,
            _ => null,
        };
        if (transport.Origin is not { } origin || path is null ||
            await transport.SignAsync(path, message.Payload, ct) is not { } assertion)
            return "Unavailable";

        using var client = clients.CreateClient(LeasedHttpDeliveryPump.ClientName);
        using var response = await LeasedHttpDeliveryPump.PostAsync(client, origin, path, message.Payload, assertion, ct);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            if (message.Kind == "Acknowledgement")
                await acknowledgements.RecordAsync(message, ct);
            return null;
        }

        return response.IsSuccessStatusCode ? "InvalidResponse" : response.StatusCode is >= HttpStatusCode.InternalServerError
            ? "Unavailable" : "Rejected";
    }

    public async Task QueueDueAsync(CancellationToken ct)
    {
        var now = EntitlementSnapshotsV1.Instant(clock);
        var due = await db.Database.SqlQuery<Binding>(
            $"SELECT * FROM public.due_merchant_entitlement_reconciliation({now})").ToListAsync(ct);
        foreach (var binding in due)
            await QueueAsync(binding, now, ct);
    }

    private async Task QueueAsync(Binding binding, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if ((await projection.EnterAsync(binding.AccountId, binding.ShopId, ct)).IsError)
            return;
        if (!await StillDueAsync(binding, now, ct))
            return;
        var shop = await db.Set<ShopLicenseState>().AsNoTracking().SingleOrDefaultAsync(row => row.ShopId == binding.ShopId, ct);
        var account = await db.Set<AccountLicenseRestriction>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.AccountId == binding.AccountId, ct);
        var request = new EntitlementReconciliationV1(1, Guid.NewGuid(), binding.AccountId, binding.ShopId,
            binding.AffiliationRequestId, account?.Revision, shop?.Revision);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, EntitlementSnapshotsV1.Json);
        await outbox.AppendAsync(request.EventId, binding.AccountId, binding.ShopId, "Reconciliation",
            bytes, SignedServiceAssertion.Digest(bytes), now, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.entitlement_reconciliation_requests (shop_id, event_id, last_queued_at, next_check_at)
            VALUES ({binding.ShopId}, {request.EventId}, {now}, {now.AddSeconds(50 + binding.ShopId.ToByteArray()[0] % 6)})
            ON CONFLICT (shop_id) DO UPDATE SET event_id = EXCLUDED.event_id,
                last_queued_at = EXCLUDED.last_queued_at, next_check_at = EXCLUDED.next_check_at
            """, ct);
        await transaction.CommitAsync(ct);
    }

    private Task<bool> StillDueAsync(Binding binding, DateTimeOffset now, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT FROM public.merchant_entitlement_bindings e
                LEFT JOIN public.shop_affiliation_delivery_receipts f ON f.event_id = e.id
                LEFT JOIN public.entitlement_reconciliation_requests r ON r.shop_id = e.shop_id
                LEFT JOIN public.entitlement_outbox o ON o.event_id = r.event_id
                LEFT JOIN public.shop_license_states s ON s.shop_id = e.shop_id AND s.account_id = e.account_id
                    AND s.affiliation_request_id = e.request_id
                WHERE e.shop_id = {binding.ShopId} AND e.account_id = {binding.AccountId}
                    AND e.request_id = {binding.AffiliationRequestId} AND e.kind = 'Accepted'
                    AND f.status IS DISTINCT FROM 'NeedsReview'
                    AND (r.shop_id IS NULL OR (o.status IN ('Delivered', 'DeadLetter') AND
                        (r.next_check_at <= {now} OR s.next_boundary > r.last_queued_at AND s.next_boundary <= {now})))
                ) AS "Value"
            """).SingleAsync(ct);

    private sealed record Binding(Guid AccountId, Guid ShopId, Guid AffiliationRequestId);
}
