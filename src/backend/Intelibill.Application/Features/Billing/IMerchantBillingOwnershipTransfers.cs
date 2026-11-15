using System.Text.Json.Serialization;
using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Billing;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MerchantOwnershipTransferRequest(Guid OperationKey, int ExpectedVersion, string? RecipientKind,
    string? Recipient, string? Reason, bool ConfirmTransfer)
{
    public override string ToString() => "[REDACTED]";
}

public interface IMerchantBillingOwnershipTransfers
{
    Task<ErrorOr<BillingOwnershipAcknowledgementV1>> InitiateAsync(MerchantBillingCaller caller, Guid account,
        MerchantOwnershipTransferRequest request, CancellationToken ct);
    Task<ErrorOr<BillingOwnershipInboxV1>> TransferInboxAsync(MerchantBillingCaller caller, Guid? after, CancellationToken ct);
    Task<ErrorOr<BillingOwnershipProposalV1>> TransferPreviewAsync(MerchantBillingCaller caller, Guid transfer, CancellationToken ct);
    Task<ErrorOr<BillingOwnershipOutcomeV1>> AcceptTransferAsync(MerchantBillingCaller caller, Guid transfer,
        AcceptBillingOwnershipTransferV1 request, CancellationToken ct);
    Task<ErrorOr<BillingOwnershipAcknowledgementV1>> RecoverTransferIssueAsync(MerchantBillingCaller caller, Guid key, CancellationToken ct);
    Task<ErrorOr<BillingOwnershipOutcomeV1>> RecoverTransferAcceptAsync(MerchantBillingCaller caller, Guid key, CancellationToken ct);
}
