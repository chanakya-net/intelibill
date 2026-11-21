using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Billing;

public interface IMerchantShopClosures
{
    Task<ErrorOr<ClosureSavedV1>> CloseAsync(MerchantBillingCaller caller, Guid shop, ShopClosureConfirmationV1 confirmation,
        CancellationToken ct);

    Task<ErrorOr<ShopClosureReviewV1>> PreviewAsync(MerchantBillingCaller caller, Guid shop, CancellationToken ct);

    Task<ErrorOr<AccountClosureStateV1>> ReadAccountStateAsync(MerchantBillingCaller caller, Guid account, CancellationToken ct);
    Task<ErrorOr<AccountClosureProposalV1>> ProposeAsync(MerchantBillingCaller caller, Guid account,
        ProposeAccountClosureV1 input, CancellationToken ct);
    Task<ErrorOr<ClosureSavedV1>> ConfirmAccountAsync(MerchantBillingCaller caller, Guid account, Guid proposal,
        ConfirmAccountClosureV1 input, CancellationToken ct);
    Task<ErrorOr<OwnerClosureContext>> ReadOwnerProposalAsync(MerchantBillingCaller caller, Guid shop, CancellationToken ct);
    Task<ErrorOr<OwnerClosureProposalV1>> RecordConsentAsync(MerchantBillingCaller caller, Guid shop, Guid proposal,
        ShopClosureConfirmationV1 input, CancellationToken ct);
}

public sealed record OwnerClosureContext(OwnerClosureProposalV1 Proposal, long OwnerVersion);
