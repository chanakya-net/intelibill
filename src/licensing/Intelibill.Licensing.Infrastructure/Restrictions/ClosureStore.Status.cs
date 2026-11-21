using Intelibill.Licensing.Application.Restrictions;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Restrictions;

internal sealed partial class ClosureStore
{
    public async Task<ClosureSavedV1[]> ListSavedAsync(Guid account, CancellationToken ct)
    {
        var saved = await db.Database.SqlQuery<SavedClosure>($"""
            SELECT id, account_id, actor_id, shop_id, operation_key, request_digest, recorded_at
            FROM public.operational_closures WHERE account_id = {account} ORDER BY recorded_at DESC, id
            """).ToArrayAsync(ct);
        var result = new List<ClosureSavedV1>(saved.Length);
        foreach (var entry in saved)
            result.Add(await ReadSavedAsync(entry, ct));
        return result.ToArray();
    }

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
            JOIN public.operational_closure_targets t ON t.closure_id = c.id AND t.account_id = c.account_id
            JOIN public.shop_affiliations a ON a.account_id = t.account_id AND a.shop_id = t.shop_id
                AND a.request_id = t.affiliation_request_id
            WHERE c.account_id = {account} AND t.shop_id = {shop}
            """).SingleAsync(ct);

    public Task<Guid[]> ClosedShopIdsAsync(Guid account, Guid[] shops, CancellationToken ct) =>
        db.Database.SqlQuery<Guid>($"""
            SELECT DISTINCT t.shop_id AS "Value" FROM public.operational_closure_targets t
            JOIN public.shop_affiliations a ON a.account_id = t.account_id AND a.shop_id = t.shop_id
                AND a.request_id = t.affiliation_request_id
            WHERE t.account_id = {account} AND t.shop_id = ANY({shops})
            """).ToArrayAsync(ct);
}
