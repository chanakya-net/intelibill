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
    LicensingEntitlementTransport transport, IHttpClientFactory clients) : ILeasedHttpDestination
{
    public bool Enabled => transport.Origin is not null;

    public async Task<string?> SendAsync(LeasedHttpMessage message, CancellationToken ct)
    {
        if (transport.Origin is not { } origin || message.Kind != "Snapshot")
            return "Unavailable";
        try
        {
            if (await transport.SignAsync(message.Payload, ct) is not { } assertion)
                return "Unavailable";
            using var client = clients.CreateClient(LeasedHttpDeliveryPump.ClientName);
            using var response = await LeasedHttpDeliveryPump.PostAsync(client, origin, EntitlementSnapshotsV1.SnapshotPath,
                message.Payload, assertion, ct);
            return await CheckResponseAsync(response, message, ct);
        }
        catch (RequestFailedException)
        {
            return "Unavailable";
        }
    }

    private static async Task<string?> CheckResponseAsync(HttpResponseMessage response, LeasedHttpMessage message, CancellationToken ct)
    {
        if (response.StatusCode != HttpStatusCode.OK)
            return response.IsSuccessStatusCode ? "InvalidResponse" :
                response.StatusCode >= HttpStatusCode.InternalServerError ? "Unavailable" : "Rejected";
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
        var due = await db.Database.SqlQuery<SourceBinding>(
            $"SELECT * FROM public.due_entitlement_verification({now})").ToListAsync(ct);
        foreach (var binding in due)
            await PublishDueAsync(binding, now, ct);
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
}
