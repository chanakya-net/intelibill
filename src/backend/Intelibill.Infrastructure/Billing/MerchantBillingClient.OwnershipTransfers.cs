using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient
{
    public async Task<ErrorOr<BillingOwnershipAcknowledgementV1>> InitiateAsync(MerchantBillingCaller caller, Guid account,
        MerchantOwnershipTransferRequest request, CancellationToken ct)
    {
        if (request.OperationKey == Guid.Empty || request.ExpectedVersion < 1 || !request.ConfirmTransfer ||
            BillingOwnershipTransfersV1.NormalizeReason(request.Reason) is not { } reason ||
            NormalizeRecipient(request.RecipientKind, request.Recipient) is not { } recipient)
            return TransferInvalid;

        var owner = await ReadInvitationAsync<BillingOwnerRevisionV1>(caller, new(BillingInvitationsV1.OwnerCheck, HttpMethod.Get,
            BillingInvitationsV1.AccountInvitations(account) + "/owner", account, []), result => result.OwnerRevision > 0, ct);
        if (owner.IsError)
            return owner.Errors;
        if (owner.Value.OwnerRevision != request.ExpectedVersion)
            return TransferStale;

        var actor = await ResolveVerifiedRecipientAsync(request.RecipientKind, recipient, ct);
        if (actor.IsError)
            return actor.Errors;
        var digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            account, request.ExpectedVersion, request.RecipientKind, recipient, reason,
        })));
        var body = JsonSerializer.SerializeToUtf8Bytes(new IssueBillingOwnershipTransferV1(request.OperationKey,
            request.ExpectedVersion, actor.Value, reason, digest, true), Json);
        return await ReadInvitationAsync<BillingOwnershipAcknowledgementV1>(caller,
            new(BillingOwnershipTransfersV1.Issue, HttpMethod.Post, BillingOwnershipTransfersV1.IssuePath(account), account, body),
            result => result.OperationKey == request.OperationKey, ct);
    }

    public Task<ErrorOr<BillingOwnershipInboxV1>> TransferInboxAsync(MerchantBillingCaller caller, Guid? after, CancellationToken ct) =>
        ReadInvitationAsync<BillingOwnershipInboxV1>(caller, new(BillingOwnershipTransfersV1.Inbox, HttpMethod.Get,
            BillingOwnershipTransfersV1.InboxPath(after), null, []),
            page => page.Transfers is { Length: <= BillingOwnershipTransfersV1.PageSize } rows && rows.All(ValidProposal), ct);

    public Task<ErrorOr<BillingOwnershipProposalV1>> TransferPreviewAsync(MerchantBillingCaller caller, Guid transfer, CancellationToken ct) =>
        ReadInvitationAsync<BillingOwnershipProposalV1>(caller, new(BillingOwnershipTransfersV1.Preview, HttpMethod.Get,
            BillingOwnershipTransfersV1.PreviewPath(transfer), null, []), result => result.TransferId == transfer && ValidProposal(result), ct);

    public Task<ErrorOr<BillingOwnershipOutcomeV1>> AcceptTransferAsync(MerchantBillingCaller caller, Guid transfer,
        AcceptBillingOwnershipTransferV1 request, CancellationToken ct) =>
        ReadInvitationAsync<BillingOwnershipOutcomeV1>(caller, new(BillingOwnershipTransfersV1.Accept, HttpMethod.Post,
            BillingOwnershipTransfersV1.AcceptPath(transfer), null, JsonSerializer.SerializeToUtf8Bytes(request, Json)),
            result => result.TransferId == transfer && ValidOutcome(result, request.OperationKey), ct);

    public Task<ErrorOr<BillingOwnershipAcknowledgementV1>> RecoverTransferIssueAsync(MerchantBillingCaller caller, Guid key,
        CancellationToken ct) => ReadInvitationAsync<BillingOwnershipAcknowledgementV1>(caller, Recovery("issue", key),
            result => result.OperationKey == key, ct);

    public Task<ErrorOr<BillingOwnershipOutcomeV1>> RecoverTransferAcceptAsync(MerchantBillingCaller caller, Guid key,
        CancellationToken ct) => ReadInvitationAsync<BillingOwnershipOutcomeV1>(caller, Recovery("accept", key),
            result => ValidOutcome(result, key), ct);

    private static BillingDelegationIntent Recovery(string action, Guid key) => new(BillingOwnershipTransfersV1.Recover,
        HttpMethod.Get, BillingOwnershipTransfersV1.OperationPath(action, key), null, []);

    private static bool ValidProposal(BillingOwnershipProposalV1? row) => row is { Revision: 1, AccountName: not null } &&
        row.TransferId != Guid.Empty && row.AccountId != Guid.Empty && row.OwnerActorId != Guid.Empty &&
        row.RecipientActorId != Guid.Empty && row.OwnerActorId != row.RecipientActorId &&
        row.ResponsibilityPolicyVersion == BillingOwnershipTransfersV1.Policy && row.ExpiresAt.Offset == TimeSpan.Zero &&
        MerchantBillingDelegationV1.NormalizeAccountName(row.AccountName) == row.AccountName;

    private static bool ValidOutcome(BillingOwnershipOutcomeV1 row, Guid key) => row.OperationKey == key &&
        row.TransferId != Guid.Empty && row.AccountId != Guid.Empty && row.Role == MerchantBillingDelegationV1.OwnerRole &&
        row.EffectiveAt.Offset == TimeSpan.Zero;

    private static Error TransferInvalid => Error.Validation("Billing.OwnershipTransferInvalid", "Review and confirm the transfer details.");
    private static Error TransferStale => Error.Conflict("Billing.OwnershipTransferConflict", "The billing authority or transfer has changed.");

    private Error MapTransferFailure(HttpStatusCode status, string? code) => (status, code) switch
    {
        (HttpStatusCode.BadRequest, "Billing.OwnershipTransferInvalid") => TransferInvalid,
        (HttpStatusCode.Conflict, "Billing.OwnershipTransferConflict") => TransferStale,
        (HttpStatusCode.Conflict, "Billing.OperationPayloadMismatch") => MerchantBillingErrors.PayloadMismatch,
        (HttpStatusCode.NotFound, "Billing.OwnershipTransferMissing") =>
            Error.NotFound("Billing.OwnershipTransferMissing", "This ownership transfer is unavailable."),
        _ => Unavailable("UpstreamTransferStatus"),
    };
}
