using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.BillingAccounts;

public sealed class BillingInvitationHandlers(IBillingDelegationAccess access, IBillingInvitationStore store)
{
    public Task<ErrorOr<BillingOwnerRevisionV1>> OwnerAsync(VerifiedBillingDelegation delegation,
        CancellationToken cancellationToken) => access.ExecuteAsync<BillingOwnerRevisionV1>(delegation, async (scope, ct) =>
        new BillingOwnerRevisionV1(await store.OwnerRevisionAsync(delegation.AccountId!.Value, scope.ActorId, ct)), cancellationToken);

    public Task<ErrorOr<BillingInvitationAcknowledgementV1>> IssueAsync(VerifiedBillingDelegation delegation,
        IssueBillingInvitationV1 request, CancellationToken cancellationToken)
    {
        if (request.OperationKey == Guid.Empty)
            return Task.FromResult<ErrorOr<BillingInvitationAcknowledgementV1>>(BillingAccountErrors.OperationKeyInvalid);
        if (request.OwnerRevision < 1 || request.InvitedActorId == Guid.Empty || request.RequestSha256 is not { Length: 64 } ||
            !request.RequestSha256.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9'))
            return Task.FromResult<ErrorOr<BillingInvitationAcknowledgementV1>>(BillingDelegationErrors.Invalid);

        return access.ExecuteAsync(delegation, (scope, ct) => store.IssueAsync(new(
            delegation.AccountId!.Value, scope.ActorId, delegation.RequestId, request, delegation.ExpiresAt), ct),
            cancellationToken);
    }

    public Task<ErrorOr<BillingInvitationInboxV1>> InboxAsync(VerifiedBillingDelegation delegation, Guid? after,
        CancellationToken cancellationToken) => access.ExecuteAsync<BillingInvitationInboxV1>(delegation,
        async (_, ct) => await store.InboxAsync(after, ct), cancellationToken);

    public Task<ErrorOr<BillingRosterV1>> RosterAsync(VerifiedBillingDelegation delegation, Guid? after,
        CancellationToken cancellationToken) => access.ExecuteAsync<BillingRosterV1>(delegation,
        async (scope, ct) => await store.RosterAsync(delegation.AccountId!.Value, scope.ActorId, after, ct), cancellationToken);

    public Task<ErrorOr<BillingInvitationProofV1>> ProofAsync(VerifiedBillingDelegation delegation, Guid invitationId,
        CancellationToken cancellationToken) => access.ExecuteAsync(delegation,
        (scope, ct) => store.ProofAsync(scope, invitationId, delegation.ExpiresAt, ct), cancellationToken);

    public Task<ErrorOr<BillingAccountV1>> AcceptAsync(VerifiedBillingDelegation delegation, Guid invitationId,
        AcceptBillingInvitationV1 request, CancellationToken cancellationToken)
    {
        if (request.ExpectedInvitationRevision != 1 || request.Token is not { Length: BillingInvitationsV1.TokenLength }
            || !request.Token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            return Task.FromResult<ErrorOr<BillingAccountV1>>(BillingInvitationErrors.Unavailable);

        return access.ExecuteAsync(delegation, (scope, ct) => store.AcceptAsync(scope, invitationId, delegation.RequestId,
            request, delegation.ExpiresAt, ct), cancellationToken);
    }
}
