using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Billing;

public interface IMerchantShopTrials
{
    Task<ErrorOr<ShopTrialPreviewV1>> PreviewTrialAsync(MerchantBillingCaller caller, Guid shop, CancellationToken ct);
    Task<ErrorOr<ShopTrialOperationV1>> AcceptTrialAsync(MerchantBillingCaller caller, Guid shop,
        ShopTrialAcceptanceV1? request, CancellationToken ct);
    Task<ErrorOr<ShopTrialOperationV1>> ReadTrialAsync(MerchantBillingCaller caller, Guid shop, Guid operation, CancellationToken ct);
}
