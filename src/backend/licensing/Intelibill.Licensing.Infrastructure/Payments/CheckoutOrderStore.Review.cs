using Intelibill.Licensing.Application.Payments;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed partial class CheckoutOrderStore
{
    public Task<ReviewSourceShop[]> ReviewShopsAsync(Guid account, Guid order, CancellationToken ct) =>
        db.Database.SqlQuery<ReviewSourceShop>($"""
            SELECT shop_id, shop_name AS name, affiliation_request_id FROM public.checkout_order_shops
            WHERE account_id = {account} AND order_id = {order} ORDER BY shop_id
            """).ToArrayAsync(ct);

    public Task<ReviewCancellationWithdrawal[]> WithdrawnRevisionsAsync(Guid account, Guid term, CancellationToken ct) =>
        db.Database.SqlQuery<ReviewCancellationWithdrawal>($"""
            SELECT revision, recorded_at FROM public.scheduled_subscription_changes
            WHERE account_id = {account} AND term_id = {term} AND kind = 'WithdrawCancellation'
            ORDER BY revision
            """).ToArrayAsync(ct);
}
