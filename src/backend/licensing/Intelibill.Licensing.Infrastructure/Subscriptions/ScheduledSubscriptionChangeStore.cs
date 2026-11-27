using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Application.Subscriptions;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Licensing.Infrastructure.Status;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Subscriptions;

internal sealed class ScheduledSubscriptionChangeStore(LicensingDbContext db, SubscriptionAccessFactsReader reader)
    : IScheduledSubscriptionChangeStore
{
    public Task<TermFactsRow?> ReadTermAsync(Guid account, Guid term, CancellationToken ct) =>
        reader.ReadTermAsync(account, term, ct);

    public Task<CancellationShop[]> ReadAssignedShopsAsync(Guid account, Guid term, CancellationToken ct) =>
        db.Database.SqlQuery<CancellationShop>($"""
            SELECT shop_id, request_id, shop_name, applied_at, coverage_ends_at
            FROM public.subscription_cancellation_scope({account}, {term})
            ORDER BY shop_id
            """).ToArrayAsync(ct);

    public Task<CancellationShopV1[]> ReadFrozenShopsAsync(Guid account, Guid change, Guid? after, CancellationToken ct) =>
        db.Database.SqlQuery<CancellationShopV1>($"""
            SELECT shop_id, shop_name FROM public.scheduled_subscription_change_shops
            WHERE account_id = {account} AND change_id = {change} AND ({after}::uuid IS NULL OR shop_id > {after}::uuid)
            ORDER BY shop_id LIMIT {SubscriptionCancellationsV1.PageSize + 1}
            """).ToArrayAsync(ct);

    public Task<CancellationReceipt?> FindReceiptAsync(Guid account, Guid key, CancellationToken ct) =>
        db.Database.SqlQuery<CancellationReceipt>($"""
            SELECT r.actor_id, r.payload_hash, c.term_id, c.kind, c.change_id, c.withdraws_change_id AS withdrawn_change_id,
                c.revision, c.effective_at, c.recorded_at, c.policy_version, c.shop_count
            FROM public.subscription_cancellation_receipts r JOIN public.scheduled_subscription_changes c USING (account_id, change_id)
            WHERE r.account_id = {account} AND r.operation_key = {key}
            """).SingleOrDefaultAsync(ct);

    public async Task AppendAsync(VerifiedBillingDelegation proof, CancellationSource source, Guid change, long revision,
        Guid operationKey, string payloadHash, DateTimeOffset at, CancellationToken ct)
    {
        var row = source.Term;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.scheduled_subscription_changes
                (change_id, account_id, term_id, revision, kind, effective_at, recorded_at, policy_version, shop_count, shop_hash)
            VALUES ({change}, {row.AccountId}, {row.TermId}, {revision}, 'ScheduleCancellation', {row.EndsAt}, {at},
                {SubscriptionCancellationsV1.Policy}, {source.Shops.Length}, {source.ShopHash})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.scheduled_subscription_change_shops (account_id, change_id, shop_id, request_id, shop_name)
            SELECT {row.AccountId}, {change}, shop_id, request_id, shop_name
            FROM unnest({source.Shops.Select(s => s.ShopId).ToArray()}::uuid[],
                {source.Shops.Select(s => s.RequestId).ToArray()}::uuid[],
                {source.Shops.Select(s => s.ShopName).ToArray()}::text[]) AS shops(shop_id, request_id, shop_name)
            """, ct);
        await AppendReceiptAsync(proof, row.AccountId, change, operationKey, payloadHash, source.SourceHash, ct);
    }

    public async Task AppendWithdrawalAsync(VerifiedBillingDelegation proof, CancellationWithdrawal withdrawal, CancellationToken ct)
    {
        var saved = withdrawal.Withdrawn;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.scheduled_subscription_changes (change_id, account_id, term_id, revision, kind, effective_at,
                recorded_at, policy_version, shop_count, shop_hash, withdraws_change_id)
            VALUES ({withdrawal.Change}, {withdrawal.AccountId}, {withdrawal.TermId}, {withdrawal.Revision},
                {CancellationReceipt.WithdrawKind}, {saved.EffectiveAt}, {withdrawal.At}, {saved.PolicyVersion},
                {saved.ShopCount}, {saved.ShopHash}, {saved.ChangeId})
            """, ct);
        await AppendReceiptAsync(proof, withdrawal.AccountId, withdrawal.Change, withdrawal.OperationKey,
            withdrawal.PayloadHash, withdrawal.SourceHash, ct);
    }

    private Task<int> AppendReceiptAsync(VerifiedBillingDelegation proof, Guid account, Guid change, Guid operationKey,
        string payloadHash, string sourceHash, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.subscription_cancellation_receipts
                (change_id, account_id, operation_key, actor_id, membership_revision, request_id, payload_hash, source_hash)
            SELECT {change}, {account}, {operationKey}, {proof.ActorId}, m.revision, {proof.RequestId}, {payloadHash}, {sourceHash}
            FROM public.billing_account_members m
            WHERE m.account_id = {account} AND m.merchant_user_id = {proof.ActorId} AND m.status = 1
            """, ct);
}
