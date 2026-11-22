using Intelibill.Licensing.Application.Restrictions;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Status;

/// <summary>
/// Merchant status reads through a verified delegation. The shop read is actor-scoped: the merchant proved live
/// shop membership before signing the exact shop path; commercial facts additionally need the actor's own live
/// Owner/Manager billing role in the shop's account, checked here independently of any shop role.
/// </summary>
public sealed class ReadSubscriptionStatusHandler(
    IBillingDelegationAccess access, ISubscriptionStatusStore store, ISubscriptionAccessFactsReader reader,
    TimeProvider clock, IEntitlementSynchronizationReader synchronization, ISuspensionStore suspensions, IClosureStore closures)
{
    public Task<ErrorOr<ShopSubscriptionStatusV1>> ReadShopAsync(VerifiedBillingDelegation delegation, Guid shop,
        CancellationToken ct)
    {
        if (delegation.AccountId is not null || delegation.Action != SubscriptionStatusV1.ShopRead || shop == Guid.Empty)
            return Task.FromResult<ErrorOr<ShopSubscriptionStatusV1>>(BillingDelegationErrors.Invalid);

        return access.ExecuteAsync<ShopSubscriptionStatusV1>(delegation, async (scope, token) =>
        {
            var at = SubscriptionStatusProjection.EvaluationInstant(clock);
            await store.UseShopAsync(shop, token);
            var coverage = await reader.ReadShopAsync(shop, token, at);
            if (coverage is null)
                return SubscriptionStatusProjection.Shop(shop, at, null, commercial: false);

            await scope.UseAccountAsync(coverage.AccountId, token);
            var commercial = await store.HoldsCommercialRoleAsync(coverage.AccountId, scope.ActorId, token);
            var sync = await synchronization.ReadAsync(coverage.AccountId, [shop], token);
            var suspension = await suspensions.ReadCustomerAsync(coverage.AccountId, shop, at, token);
            var closure = await closures.ReadShopClosureStatusAsync(coverage.AccountId, shop, token);
            return SubscriptionStatusProjection.Shop(shop, at, coverage, commercial, sync.GetValueOrDefault(shop)) with
            {
                Suspension = suspension,
                ClosedAt = closure.ClosedAt,
                ReopenedAt = closure.ReopenedAt,
                ReopeningRequiredRevision = closure.ReopeningRevision,
            };
        }, ct);
    }

    public Task<ErrorOr<AccountSubscriptionStatusV1>> ReadAccountAsync(VerifiedBillingDelegation delegation, Guid account,
        Guid? after, CancellationToken ct)
    {
        if (delegation.AccountId != account || delegation.Action != SubscriptionStatusV1.AccountRead || after == Guid.Empty)
            return Task.FromResult<ErrorOr<AccountSubscriptionStatusV1>>(BillingDelegationErrors.Invalid);

        // The delegation transaction already holds the actor's live Owner/Manager membership FOR SHARE.
        return access.ExecuteAsync<AccountSubscriptionStatusV1>(delegation, async (_, token) =>
        {
            var at = SubscriptionStatusProjection.EvaluationInstant(clock);
            var rows = await reader.ReadAccountAsync(account, after, SubscriptionStatusV1.PageSize + 1, token);
            var sync = await synchronization.ReadPageAsync(account, rows, token);
            return SubscriptionStatusProjection.Account(account, at, rows, commercial: true, sync);
        }, ct);
    }
}
