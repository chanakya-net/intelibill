using ErrorOr;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Billing;

/// <summary>
/// Observe-only licensing status. The shop read needs the caller's live membership of the active shop it names;
/// the account read needs live billing membership, checked by licensing. Neither gates any merchant action.
/// </summary>
public interface IMerchantSubscriptionStatus
{
    Task<ErrorOr<ShopSubscriptionStatusV1>> ReadShopAsync(MerchantBillingCaller caller, Guid shop, CancellationToken ct);

    Task<ErrorOr<AccountSubscriptionStatusV1>> ReadAccountAsync(MerchantBillingCaller caller, Guid account, Guid? after,
        CancellationToken ct);
}

public static class MerchantSubscriptionStatusErrors
{
    public static Error ShopScope => Errors.Licensing.ShopScopeRequired;
}
