using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Restrictions;

internal sealed partial class ClosureStore
{
    public async Task<ProviderClosureV1[]> ReadProviderClosuresAsync(Guid account, CancellationToken ct)
    {
        var rows = await db.Database.SqlQueryRaw<ProviderClosureRow>(ProviderClosureHistorySql, account).ToArrayAsync(ct);
        return rows.GroupBy(row => (row.ClosureId, row.Scope, row.SavedAt, row.FinancialTreatment)).Select(group =>
            new ProviderClosureV1(group.Key.ClosureId, group.Key.Scope, group.Key.SavedAt, group.Key.FinancialTreatment,
                [.. group.Select(ToShop)])).ToArray();
    }

    private static ProviderClosureShopV1 ToShop(ProviderClosureRow row) =>
        new(row.ShopId, row.ShopName, row.ClosurePropagation, row.ClosureRevision, row.AppliedClosureRevision,
            row.ClosureAcknowledgedAt, row.ReopeningId is { } id
                ? new(id, row.ReopenedAt!.Value, row.ReopeningPropagation!) : null);

    // Keep the retained snapshot range bounded by the decision that changed its meaning.
    // An Applied receipt may advance only the account head. Require the receipt's exact shop revision.
    private const string ProviderClosureHistorySql = """
        SELECT c.id AS closure_id, CASE WHEN c.shop_id IS NULL THEN 'Account' ELSE 'Shop' END AS scope,
            c.recorded_at AS saved_at, c.financial_treatment, t.shop_id, t.shop_name,
            CASE WHEN closed_receipt.shop_revision IS NOT NULL THEN 'Applied'
                WHEN r.id IS NOT NULL THEN 'Superseded'
                WHEN closure_delivery.status IN ('NeedsReview', 'DeadLetter') THEN 'NeedsReview'
                ELSE 'Pending' END AS closure_propagation,
            closed.shop_revision AS closure_revision, closed_receipt.shop_revision AS applied_closure_revision,
            closed_receipt.applied_at AS closure_acknowledged_at, r.id AS reopening_id, r.reopened_at,
            CASE WHEN reopening_receipt.shop_revision IS NOT NULL THEN 'Acknowledged'
                WHEN latest.closure_revision > reopened.shop_revision THEN 'Superseded'
                WHEN reopening_delivery.status IN ('NeedsReview', 'DeadLetter') THEN 'NeedsReview'
                ELSE 'Pending' END AS reopening_propagation
        FROM public.operational_closures c
        JOIN public.operational_closure_targets t ON t.closure_id = c.id AND t.account_id = c.account_id
        LEFT JOIN public.operational_closure_reopenings r ON r.closure_id = t.closure_id AND r.shop_id = t.shop_id
        LEFT JOIN public.entitlement_snapshots closed ON closed.event_id = t.event_id
        LEFT JOIN public.entitlement_snapshots reopened ON reopened.event_id = r.event_id
        LEFT JOIN public.entitlement_outbox closure_delivery ON closure_delivery.event_id = closed.event_id
        LEFT JOIN public.entitlement_outbox reopening_delivery ON reopening_delivery.event_id = reopened.event_id
        LEFT JOIN public.entitlement_shop_heads h ON h.account_id = c.account_id AND h.shop_id = t.shop_id
        LEFT JOIN public.entitlement_snapshots latest ON latest.shop_id = h.shop_id AND latest.shop_revision = h.revision
            AND latest.account_id = t.account_id AND latest.affiliation_request_id = t.affiliation_request_id
        LEFT JOIN LATERAL (
            SELECT s.shop_revision, s.applied_at
            FROM public.applied_entitlement_shop_receipts s
            WHERE s.shop_id = t.shop_id AND s.account_id = t.account_id
                AND s.affiliation_request_id = t.affiliation_request_id AND s.shop_revision >= closed.shop_revision
                AND s.shop_revision < coalesce(reopened.shop_revision, 9223372036854775807)
            ORDER BY s.shop_revision DESC LIMIT 1
        ) closed_receipt ON true
        LEFT JOIN LATERAL (
            SELECT s.shop_revision FROM public.applied_entitlement_shop_receipts s
            WHERE s.shop_id = t.shop_id AND s.account_id = t.account_id
                AND s.affiliation_request_id = t.affiliation_request_id AND s.closure_revision = reopened.shop_revision
            ORDER BY s.shop_revision DESC LIMIT 1
        ) reopening_receipt ON true
        WHERE c.account_id = {0}
        ORDER BY c.recorded_at DESC, c.id, t.shop_id
        """;

    private sealed record ProviderClosureRow(Guid ClosureId, string Scope, DateTimeOffset SavedAt, string FinancialTreatment,
        Guid ShopId, string ShopName, string ClosurePropagation, long? ClosureRevision, long? AppliedClosureRevision,
        DateTimeOffset? ClosureAcknowledgedAt, Guid? ReopeningId, DateTimeOffset? ReopenedAt, string? ReopeningPropagation);
}
