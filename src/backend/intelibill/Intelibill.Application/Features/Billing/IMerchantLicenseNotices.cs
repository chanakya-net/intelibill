using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Billing;

/// <summary>
/// Read-only shop and account licence notices plus narrow billing contact consent relay.
/// Shop notice reads require live active-shop membership and return operational guidance only.
/// </summary>
public interface IMerchantLicenseNotices
{
    Task<ErrorOr<ShopLicenseNoticesV1>> ReadShopNoticesAsync(
        MerchantBillingCaller caller,
        Guid shopId,
        Guid? after,
        CancellationToken ct);

    Task<ErrorOr<AccountLicenseNoticesV1>> ReadAccountNoticesAsync(
        MerchantBillingCaller caller,
        Guid accountId,
        Guid? after,
        CancellationToken ct);

    Task<ErrorOr<BillingContactsPageV1>> ListContactsAsync(
        MerchantBillingCaller caller,
        Guid accountId,
        Guid? after,
        CancellationToken ct);

    Task<ErrorOr<BillingContactV1>> ApproveContactAsync(
        MerchantBillingCaller caller,
        Guid accountId,
        ApproveBillingContactRequestV1 request,
        CancellationToken ct);

    Task<ErrorOr<BillingContactV1>> RevokeContactByOwnerAsync(
        MerchantBillingCaller caller,
        Guid accountId,
        Guid contactId,
        RevokeBillingContactRequestV1 request,
        CancellationToken ct);

    Task<ErrorOr<BillingContactV1>> ConfirmSelfConsentAsync(
        MerchantBillingCaller caller,
        Guid contactId,
        ConfirmBillingContactConsentRequestV1 request,
        CancellationToken ct);

    Task<ErrorOr<BillingContactV1>> RevokeContactByHolderAsync(
        MerchantBillingCaller caller,
        Guid contactId,
        RevokeBillingContactRequestV1 request,
        CancellationToken ct);
}
