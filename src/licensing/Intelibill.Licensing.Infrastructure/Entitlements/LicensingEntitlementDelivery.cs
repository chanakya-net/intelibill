using System.Net;
using System.Text.Json;
using Azure;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Entitlements;

internal sealed class LicensingEntitlementDelivery(LicensingDbContext db, EntitlementPublisher publisher,
    LicensingEntitlementTransport transport, IHttpClientFactory clients,
    Subscriptions.ScheduledCapacityReductionStore reductions) : ILeasedHttpDestination
{
    public bool Enabled => transport.Origin is not null;

    public async Task<string?> SendAsync(LeasedHttpMessage message, CancellationToken ct)
    {
        var path = message.Kind switch
        {
            "Snapshot" => EntitlementSnapshotsV1.SnapshotPath,
            CapacityReservationV1.Reserve => CapacityReservationV1.ReservePath,
            CapacityReservationV1.Consume => CapacityReservationV1.ConsumePath,
            ReductionTransportV1.ManifestKind => ReductionTransportV1.ManifestPath,
            ReductionTransportV1.ReserveKind => ReductionTransportV1.ReservePath,
            ReductionTransportV1.ConsumeKind => ReductionTransportV1.ConsumePath,
            _ => null,
        };
        if (transport.Origin is not { } origin || path is null)
            return "Unavailable";
        try
        {
            if (await transport.SignAsync(message.Payload, ct, path) is not { } assertion)
                return "Unavailable";
            using var client = clients.CreateClient(LeasedHttpDeliveryPump.ClientName);
            using var response = await LeasedHttpDeliveryPump.PostAsync(client, origin, path,
                message.Payload, assertion, ct);
            if (message.Kind == "Snapshot") return await CheckResponseAsync(response, message, ct);
            return response.StatusCode == HttpStatusCode.NoContent ? null : DeliveryFailure(response);
        }
        catch (RequestFailedException)
        {
            return "Unavailable";
        }
    }

    private static string DeliveryFailure(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? "InvalidResponse" :
            response.StatusCode >= HttpStatusCode.InternalServerError ? "Unavailable" : "Rejected";

    private async Task<string?> CheckResponseAsync(HttpResponseMessage response, LeasedHttpMessage message, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.Locked)
        {
            var snapshot = JsonSerializer.Deserialize<EntitlementSnapshotV1>(message.Payload, EntitlementSnapshotsV1.Json);
            return snapshot?.Shop is { ReplacementId: not null, AccessEndsAt: { } end } && publisher.Now < end
                ? CoverageReplacementsV1.AwaitingRelease : "Rejected";
        }
        if (response.StatusCode != HttpStatusCode.OK)
            return DeliveryFailure(response);
        if (response.Content.Headers.ContentType?.MediaType != "application/json" ||
            response.Content.Headers.ContentLength > EntitlementSnapshotsV1.BodyLimit)
            return "InvalidResponse";

        var buffer = new byte[EntitlementSnapshotsV1.BodyLimit + 1];
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var length = await stream.ReadAtLeastAsync(buffer, buffer.Length, false, ct);
        if (length == 0 || length > EntitlementSnapshotsV1.BodyLimit)
            return "InvalidResponse";
        try
        {
            var ack = JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(buffer.AsSpan(0, length), EntitlementSnapshotsV1.Json);
            return ack is { SchemaVersion: 1 } && ack.EventId == message.EventId &&
                ack.Digest == SignedServiceAssertion.Digest(message.Payload) ? null : "InvalidResponse";
        }
        catch (JsonException)
        {
            return "InvalidResponse";
        }
    }

    public async Task QueueDueAsync(CancellationToken ct)
    {
        var now = publisher.Now;
        var dueReductions = await db.Database.SqlQuery<DueReduction>(
            $"SELECT * FROM public.due_scheduled_capacity_reductions({now})").ToListAsync(ct);
        foreach (var item in dueReductions)
            await QueueDueReductionAsync(item, now, ct);

        var due = await db.Database.SqlQuery<SourceBinding>(
            $"SELECT * FROM public.due_entitlement_verification({now})").ToListAsync(ct);
        foreach (var binding in due)
            await PublishDueAsync(binding, now, ct);
    }

    private async Task QueueDueReductionAsync(DueReduction item, DateTimeOffset now, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await EntitlementMerchantEvents.EnterAccountAsync(db, item.AccountId, ct);
        if (await reductions.QueueDueAttemptAsync(item.AccountId, item.ScheduleId, now, ct))
            await tx.CommitAsync(ct);
    }

    private async Task PublishDueAsync(SourceBinding binding, DateTimeOffset now, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await EntitlementMerchantEvents.EnterAccountAsync(db, binding.AccountId, ct);
        var due = await db.Database.SqlQuery<bool>($"""
            SELECT NOT EXISTS (SELECT FROM public.entitlement_shop_heads WHERE account_id = {binding.AccountId}
                AND shop_id = {binding.ShopId} AND next_verification_at > {now}) AS "Value"
            """).SingleAsync(ct);
        if (due && await publisher.PublishAsync(binding.AccountId, binding.ShopId, reconcile: true, ct))
            await tx.CommitAsync(ct);
    }

    private sealed record SourceBinding(Guid AccountId, Guid ShopId);
    private sealed record DueReduction(Guid AccountId, Guid ScheduleId);
}
