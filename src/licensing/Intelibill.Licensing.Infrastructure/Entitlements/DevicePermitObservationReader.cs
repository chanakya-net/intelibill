using Intelibill.Licensing.Application.Coverage;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Entitlements;

/// <summary>
/// Aggregates the retained, append-only observations per shop and opaque device reference in account-scoped
/// statements. Only observations bound to the shop's current affiliation are read, so evidence gathered under
/// an earlier account never appears under a later one.
/// </summary>
internal sealed class DevicePermitObservationReader(LicensingDbContext db) : IDevicePermitObservationReader
{
    public async Task<DevicePermitPage?> ReadAsync(Guid account, Guid? shop, DateTimeOffset evaluatedAt,
        (Guid Shop, string Device)? after, int take, CancellationToken ct)
    {
        if (shop is { } named && !await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.current_shop_affiliations WHERE account_id = {account} AND shop_id = {named}) AS "Value"
            """).SingleAsync(ct))
            return null;

        var afterShop = after?.Shop;
        var afterDevice = after?.Device;
        var rows = await db.Database.SqlQuery<DevicePermitRow>($"""
            WITH bound AS (
                SELECT o.*, a.shop_name FROM public.offline_device_observations o
                JOIN public.current_shop_affiliations a ON a.account_id = o.account_id AND a.shop_id = o.shop_id
                    AND a.request_id = o.affiliation_request_id
                WHERE o.account_id = {account} AND ({shop}::uuid IS NULL OR o.shop_id = {shop}::uuid)
                    AND ({afterShop}::uuid IS NULL OR (o.shop_id, o.device_ref) > ({afterShop}::uuid, {afterDevice}::text))
            ), devices AS (
                SELECT shop_id, device_ref, min(shop_name) AS shop_name, max(observed_at) AS last_contact_at,
                    max(observed_at) FILTER (WHERE observation = 'OfflineSync') AS last_synchronized_at,
                    max(observed_at) FILTER (WHERE observation = 'PermitIssued') AS last_permit_issued_at,
                    max(valid_until) AS known_permit_end,
                    (count(*) FILTER (WHERE valid_until > {evaluatedAt}))::int AS outstanding_permits
                FROM bound GROUP BY shop_id, device_ref ORDER BY shop_id, device_ref LIMIT {take}
            )
            SELECT d.shop_id, d.shop_name, d.device_ref, d.last_contact_at, d.last_synchronized_at,
                d.last_permit_issued_at, d.known_permit_end, d.outstanding_permits, deciding.permit_limit AS "limit",
                deciding.account_revision, deciding.shop_revision, deciding.authority_verified_at
            FROM devices d
            LEFT JOIN LATERAL (
                SELECT b.permit_limit, b.account_revision, b.shop_revision, b.authority_verified_at FROM bound b
                WHERE b.shop_id = d.shop_id AND b.device_ref = d.device_ref AND b.valid_until = d.known_permit_end
                ORDER BY b.observed_at DESC, b.event_id LIMIT 1
            ) deciding ON true
            ORDER BY d.shop_id, d.device_ref
            """).ToListAsync(ct);
        var since = await db.Database.SqlQuery<DateTimeOffset?>($"""
            SELECT min(observed_at) AS "Value" FROM public.offline_device_observations WHERE account_id = {account}
            """).SingleAsync(ct);
        return new(rows, since);
    }
}
