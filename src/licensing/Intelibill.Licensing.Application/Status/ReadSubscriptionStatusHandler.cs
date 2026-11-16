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
    IBillingDelegationAccess access, ISubscriptionStatusStore store, ISubscriptionAccessFactsReader reader, TimeProvider clock)
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
            var coverage = await reader.ReadShopAsync(shop, token);
            if (coverage is null)
                return SubscriptionStatusProjection.Shop(shop, at, null, commercial: false);

            await scope.UseAccountAsync(coverage.AccountId, token);
            var commercial = await store.HoldsCommercialRoleAsync(coverage.AccountId, scope.ActorId, token);
            return SubscriptionStatusProjection.Shop(shop, at, coverage, commercial);
        }, ct);
    }

    public Task<ErrorOr<AccountSubscriptionStatusV1>> ReadAccountAsync(VerifiedBillingDelegation delegation, Guid account,
        Guid? after, CancellationToken ct)
    {
        if (delegation.AccountId != account || delegation.Action != SubscriptionStatusV1.AccountRead || after == Guid.Empty)
            return Task.FromResult<ErrorOr<AccountSubscriptionStatusV1>>(BillingDelegationErrors.Invalid);

        // The delegation transaction already holds the actor's live Owner/Manager membership FOR SHARE.
        return access.ExecuteAsync<AccountSubscriptionStatusV1>(delegation, async (_, token) =>
            SubscriptionStatusProjection.Account(account, SubscriptionStatusProjection.EvaluationInstant(clock),
                await reader.ReadAccountAsync(account, after, SubscriptionStatusV1.PageSize + 1, token), commercial: true), ct);
    }
}
