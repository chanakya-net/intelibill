using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Licensing;

/// <summary>
/// Queues what licensing may know about a device's offline permission, in the caller's own transaction:
/// a stored permit's window, or that this server received an offline upload. Only the opaque device
/// reference, server instants and entitlement revisions are written; no invoice, customer, stock, user,
/// session, lease or snapshot detail is. A shop with no licence binding reports nothing.
/// </summary>
internal sealed class OfflineDeviceObservationOutbox(ApplicationDbContext db)
{
    private readonly HashSet<string> _reportedSyncs = [];

    internal async Task AppendPermitAsync(OfflineLicensePermit permit, CancellationToken ct)
    {
        var grant = await db.Set<OfflineLicenseGrant>().AsNoTracking()
            .SingleAsync(row => row.ShopId == permit.ShopId && row.Id == permit.GrantId, ct);
        var window = new OfflineDevicePermitWindowV1(permit.Id, permit.NotBefore, permit.ValidUntil,
            OfflineDeviceObservationsV1.Limit(permit.LimitingReason), permit.CurrentAccountRevision,
            permit.CurrentShopRevision, grant.AuthorityVerifiedAt);
        await AppendAsync(new(OfflineDeviceObservationsV1.SchemaVersion, Guid.NewGuid(), grant.AccountId, permit.ShopId,
            grant.AffiliationRequestId, OfflineDeviceObservationsV1.DeviceRef(permit.ShopId, permit.DeviceId),
            OfflineDeviceObservationsV1.PermitIssued, permit.NotBefore, window), ct);
    }

    /// <summary>At most one per device in a request, however many invoices the upload carried.</summary>
    internal async Task AppendSyncAsync(Guid shopId, string deviceId, DateTimeOffset receivedAt, CancellationToken ct)
    {
        var device = OfflineDeviceObservationsV1.DeviceRef(shopId, deviceId);
        if (db.Database.CurrentTransaction is null || _reportedSyncs.Contains(device))
            return;
        var binding = await db.Set<ShopLicenseState>().AsNoTracking().Where(row => row.ShopId == shopId)
            .Select(row => new { row.AccountId, row.AffiliationRequestId }).SingleOrDefaultAsync(ct);
        if (binding is null)
            return;
        await AppendAsync(new(OfflineDeviceObservationsV1.SchemaVersion, Guid.NewGuid(), binding.AccountId, shopId,
            binding.AffiliationRequestId, device, OfflineDeviceObservationsV1.OfflineSync,
            OfflineDeviceObservationsV1.Instant(receivedAt), null), ct);
        _reportedSyncs.Add(device);
    }

    private async Task AppendAsync(OfflineDeviceObservationV1 observation, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(observation, EntitlementSnapshotsV1.Json);
        await new LeasedHttpOutbox(db).AppendAsync(observation.EventId, observation.AccountId, observation.ShopId,
            OfflineDeviceObservationsV1.Kind, bytes, SignedServiceAssertion.Digest(bytes), observation.ObservedAt, ct);
    }
}
