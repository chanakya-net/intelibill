using Intelibill.Licensing.Application.Restrictions;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Restrictions;

internal sealed partial class ClosureStore
{
    public Task<bool> ReopeningPublishersReadyAsync(CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"SELECT public.reopening_publishers_ready() AS \"Value\"").SingleAsync(ct);

    public Task<ShopClosureStatus> ReadShopClosureStatusAsync(Guid account, Guid shop, CancellationToken ct) =>
        db.Database.SqlQuery<ShopClosureStatus>($"""
            SELECT closed.closed_at, reopening.reopened_at, reopening.shop_revision AS reopening_revision
            FROM (VALUES (1)) AS singleton(id)
            LEFT JOIN public.current_shop_affiliations a ON a.account_id = {account} AND a.shop_id = {shop}
            LEFT JOIN LATERAL (
                SELECT min(c.recorded_at) AS closed_at FROM public.active_operational_closure_targets t
                JOIN public.operational_closures c ON c.id = t.closure_id AND c.account_id = t.account_id
                WHERE t.account_id = a.account_id AND t.shop_id = a.shop_id AND t.affiliation_request_id = a.request_id
            ) closed ON true
            LEFT JOIN LATERAL (
                SELECT r.reopened_at, s.shop_revision FROM public.operational_closure_reopenings r
                JOIN public.entitlement_snapshots s ON s.event_id = r.event_id
                JOIN public.entitlement_shop_heads h ON h.account_id = r.account_id AND h.shop_id = r.shop_id
                JOIN public.entitlement_snapshots latest ON latest.shop_id = h.shop_id AND latest.shop_revision = h.revision
                    AND latest.account_id = r.account_id AND latest.affiliation_request_id = r.affiliation_request_id
                WHERE r.account_id = a.account_id AND r.shop_id = a.shop_id AND r.affiliation_request_id = a.request_id
                    AND latest.closure_revision = s.shop_revision
            ) reopening ON true
            """).SingleAsync(ct);
}
