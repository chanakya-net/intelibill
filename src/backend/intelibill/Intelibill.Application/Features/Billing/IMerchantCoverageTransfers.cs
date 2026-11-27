using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Billing;

public enum CoverageTransferShopAction { Read, Accept, Revoke }

/// <summary>
/// P06-07a merchant bridge. Billing-owner actions are delegated to licensing under the caller's live session;
/// the named shop owner's consent is decided against live merchant membership and delivered by the outbox.
/// </summary>
public interface IMerchantCoverageTransfers
{
    Task<ErrorOr<CoverageTransferPreviewV1>> PreviewCoverageTransferAsync(MerchantBillingCaller caller, Guid account,
        Guid term, PreviewCoverageTransferV1 query, CancellationToken ct);

    Task<ErrorOr<CoverageTransferV1>> RequestCoverageTransferAsync(MerchantBillingCaller caller, Guid account,
        Guid term, RequestCoverageTransferV1 command, CancellationToken ct);

    Task<ErrorOr<CoverageTransferV1>> RecoverCoverageTransferAsync(MerchantBillingCaller caller, Guid account,
        Guid term, Guid key, CancellationToken ct);

    Task<ErrorOr<CoverageTransferListV1>> CoverageTransfersAsync(MerchantBillingCaller caller, Guid account, CancellationToken ct);

    Task<ErrorOr<CoverageTransferV1>> CoverageTransferAsync(MerchantBillingCaller caller, Guid account, Guid transfer,
        CancellationToken ct);

    Task<ErrorOr<CoverageTransferV1>> DecideCoverageTransferAsync(MerchantBillingCaller caller, Guid account, Guid transfer,
        string purpose, string kind, CoverageTransferDecisionV1 decision, CancellationToken ct);

    Task<ErrorOr<CoverageTransferV1>> RecoverCoverageTransferDecisionAsync(MerchantBillingCaller caller, Guid account,
        Guid transfer, string purpose, Guid key, CancellationToken ct);

    Task<ErrorOr<CoverageTransferShopOwnerV1>> CoverageTransferShopConsentAsync(MerchantBillingCaller caller, Guid account,
        Guid shop, Guid transfer, CoverageTransferShopAction action, CoverageTransferDecisionV1? decision, CancellationToken ct);
}
