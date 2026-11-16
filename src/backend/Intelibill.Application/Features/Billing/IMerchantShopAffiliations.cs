using ErrorOr;
using Intelibill.Shared.Contracts.V1;

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
    public static Error OwnerRequired => Error.Forbidden("Billing.ShopOwnerRequired", "Select the shop you currently own.");
    public static Error Invalid => Error.Validation("Billing.AffiliationInvalid", "Select a shop and one coverage choice.");
    public static Error Missing => Error.NotFound("Billing.AffiliationMissing", "The affiliation request was not found.");
    public static Error Conflict => Error.Conflict("Billing.AffiliationConflict", "The affiliation selection is no longer available.");
    public static Error ConsentInvalid => Error.Conflict("Billing.ConsentInvalid", "A current, unrevoked consent is required.");
}
