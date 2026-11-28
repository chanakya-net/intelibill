using ErrorOr;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Billing;

public enum ShopAffiliationConsentAction { Read, Grant, Revoke, Accept }

public interface IMerchantShopAffiliations
{
    Task<ErrorOr<AccountAffiliationsV1>> ListAsync(MerchantBillingCaller caller, Guid account, CancellationToken ct);
    Task<ErrorOr<ShopAffiliationRequestV1>> RequestAsync(MerchantBillingCaller caller, Guid account,
        RequestShopAffiliationV1 request, CancellationToken ct);
    Task<ErrorOr<ShopAffiliationConsentV1>> ConsentAsync(MerchantBillingCaller caller, Guid account, Guid shop,
        Guid request, ShopAffiliationConsentAction action, Guid operation, CancellationToken ct);
}

public static class ShopAffiliationConsentErrors
{
    public static Error OwnerRequired => Errors.Billing.ShopOwnerRequired;
    public static Error Invalid => Errors.Billing.AffiliationInvalid;
    public static Error Missing => Errors.Billing.AffiliationMissing;
    public static Error Conflict => Errors.Billing.AffiliationConflict;
    public static Error ConsentInvalid => Errors.Billing.ConsentInvalid;
}
