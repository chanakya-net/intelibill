using Intelibill.Licensing.Application.Restrictions;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Restrictions;

internal sealed partial class ClosureStore
{
    public async Task<ClosedBillingAccountV1?> ReadAccountClosureAsync(Guid account, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<SavedClosure>($"""
            SELECT id, account_id, actor_id, shop_id, operation_key, request_digest, recorded_at
            FROM public.operational_closures WHERE account_id = {account} AND shop_id IS NULL
            """).SingleOrDefaultAsync(ct);
        if (row is null) return null;

        var saved = await ReadSavedAsync(row, ct);
        return new(account, saved.SavedAt, saved.FinancialTreatment,
            [.. saved.Shops.Select(shop => new ClosedBillingAccountShopV1(shop.ShopId, shop.ShopName))]);
    }

    public Task<DateTimeOffset?> ClosedAtAsync(Guid account, Guid shop, CancellationToken ct) =>
        db.Database.SqlQuery<DateTimeOffset?>($"""
            SELECT min(c.recorded_at) AS "Value" FROM public.operational_closures c
            JOIN public.active_operational_closure_targets t ON t.closure_id = c.id AND t.account_id = c.account_id
            JOIN public.current_shop_affiliations a ON a.account_id = t.account_id AND a.shop_id = t.shop_id
                AND a.request_id = t.affiliation_request_id
            WHERE c.account_id = {account} AND t.shop_id = {shop}
            """).SingleAsync(ct);

    public Task<CheckoutClosureStatus[]> ReadCheckoutClosuresAsync(Guid account, Guid[] shops, CancellationToken ct) =>
        db.Database.SqlQuery<CheckoutClosureStatus>($"""
            SELECT a.shop_id, EXISTS (SELECT FROM public.active_operational_closure_targets t
                WHERE t.account_id = a.account_id AND t.shop_id = a.shop_id AND t.affiliation_request_id = a.request_id) AS closed,
                opening.revision AS reopening_revision,
                EXISTS (SELECT FROM public.applied_entitlement_shop_receipts receipt
                    WHERE receipt.account_id = a.account_id AND receipt.shop_id = a.shop_id
                        AND receipt.affiliation_request_id = a.request_id
                        AND receipt.closure_revision = opening.revision) AS reopening_acknowledged
            FROM public.current_shop_affiliations a
            LEFT JOIN LATERAL (
                SELECT max(s.shop_revision) AS revision FROM public.operational_closure_reopenings r
                JOIN public.entitlement_snapshots s ON s.event_id = r.event_id
                WHERE r.account_id = a.account_id AND r.shop_id = a.shop_id
                    AND r.affiliation_request_id = a.request_id AND r.opens_shop
            ) opening ON true
            WHERE a.account_id = {account} AND a.shop_id = ANY({shops})
            """).ToArrayAsync(ct);
}
