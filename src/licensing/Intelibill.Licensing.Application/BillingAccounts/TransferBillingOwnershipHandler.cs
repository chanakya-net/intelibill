using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.BillingAccounts;

public static class BillingOwnershipTransferErrors
{
    public static Error Invalid => Error.Validation("Billing.OwnershipTransferInvalid", "Review and confirm the transfer details.");
    public static Error Missing => Error.NotFound("Billing.OwnershipTransferMissing", "This ownership transfer is unavailable.");
    public static Error Stale => Error.Conflict("Billing.OwnershipTransferConflict", "The billing authority or transfer has changed.");
}

public interface IBillingOwnershipTransferStore
{
    Task<ErrorOr<BillingOwnershipAcknowledgementV1>> IssueAsync(VerifiedBillingDelegation proof, Guid account,
        IssueBillingOwnershipTransferV1 request, CancellationToken ct);
    Task<BillingOwnershipInboxV1> InboxAsync(Guid? after, CancellationToken ct);
    Task<ErrorOr<BillingOwnershipProposalV1>> PreviewAsync(Guid transfer, CancellationToken ct);
    Task<ErrorOr<BillingOwnershipOutcomeV1>> AcceptAsync(IBillingDelegationScope scope, VerifiedBillingDelegation proof,
        Guid transfer, AcceptBillingOwnershipTransferV1 request, CancellationToken ct);
    Task<ErrorOr<BillingOwnershipAcknowledgementV1>> RecoverIssueAsync(Guid key, CancellationToken ct);
    Task<ErrorOr<BillingOwnershipOutcomeV1>> RecoverAcceptAsync(Guid key, CancellationToken ct);
}

public sealed class TransferBillingOwnershipHandler(IBillingDelegationAccess access, IBillingOwnershipTransferStore store)
{
    public Task<ErrorOr<BillingOwnershipAcknowledgementV1>> IssueAsync(VerifiedBillingDelegation proof, Guid account,
        IssueBillingOwnershipTransferV1 request, CancellationToken ct) =>
        request.OperationKey == Guid.Empty || request.ExpectedVersion < 1 || !request.ConfirmTransfer ||
        !IsDigest(request.RequestSha256) || BillingOwnershipTransfersV1.NormalizeReason(request.Reason) is not { } reason
            ? Task.FromResult<ErrorOr<BillingOwnershipAcknowledgementV1>>(BillingOwnershipTransferErrors.Invalid)
            : access.ExecuteAsync(proof, (_, token) => store.IssueAsync(proof, account, request with { Reason = reason }, token), ct);

    public Task<ErrorOr<BillingOwnershipInboxV1>> InboxAsync(VerifiedBillingDelegation proof, Guid? after, CancellationToken ct) =>
        access.ExecuteAsync<BillingOwnershipInboxV1>(proof, async (_, token) => await store.InboxAsync(after, token), ct);

    public Task<ErrorOr<BillingOwnershipProposalV1>> PreviewAsync(VerifiedBillingDelegation proof, Guid transfer, CancellationToken ct) =>
        access.ExecuteAsync(proof, (_, token) => store.PreviewAsync(transfer, token), ct);

    public Task<ErrorOr<BillingOwnershipOutcomeV1>> AcceptAsync(VerifiedBillingDelegation proof, Guid transfer,
        AcceptBillingOwnershipTransferV1 request, CancellationToken ct) =>
        request.OperationKey == Guid.Empty || request.ExpectedVersion < 1 || !request.AcceptResponsibility ||
        request.ResponsibilityPolicyVersion != BillingOwnershipTransfersV1.Policy
            ? Task.FromResult<ErrorOr<BillingOwnershipOutcomeV1>>(BillingOwnershipTransferErrors.Invalid)
            : access.ExecuteAsync(proof, (scope, token) => store.AcceptAsync(scope, proof, transfer, request, token), ct);

    public Task<ErrorOr<BillingOwnershipAcknowledgementV1>> RecoverIssueAsync(VerifiedBillingDelegation proof, Guid key,
        CancellationToken ct) => access.ExecuteAsync(proof, (_, token) => store.RecoverIssueAsync(key, token), ct);

    public Task<ErrorOr<BillingOwnershipOutcomeV1>> RecoverAcceptAsync(VerifiedBillingDelegation proof, Guid key,
        CancellationToken ct) => access.ExecuteAsync(proof, (_, token) => store.RecoverAcceptAsync(key, token), ct);

    private static bool IsDigest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
}
