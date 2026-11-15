using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.BillingAccounts;

public sealed record BillingInvitationIssue(Guid AccountId, Guid ActorId, Guid RequestId,
    IssueBillingInvitationV1 Request, DateTimeOffset DelegationExpiry);

public interface IBillingInvitationStore
{
    Task<int> OwnerRevisionAsync(Guid accountId, Guid actorId, CancellationToken cancellationToken);
    Task<ErrorOr<BillingInvitationAcknowledgementV1>> IssueAsync(BillingInvitationIssue command,
        CancellationToken cancellationToken);
    Task<BillingInvitationInboxV1> InboxAsync(Guid? after, CancellationToken cancellationToken);
    Task<ErrorOr<BillingInvitationProofV1>> ProofAsync(IBillingDelegationScope scope, Guid invitationId,
        DateTimeOffset delegationExpiry, CancellationToken cancellationToken);
    Task<ErrorOr<BillingAccountV1>> AcceptAsync(IBillingDelegationScope scope, Guid invitationId, Guid requestId,
        AcceptBillingInvitationV1 request, DateTimeOffset delegationExpiry, CancellationToken cancellationToken);
    Task<BillingRosterV1> RosterAsync(Guid accountId, Guid actorId, Guid? after, CancellationToken cancellationToken);
}

public static class BillingInvitationErrors
{
    public static Error RevisionConflict => Error.Conflict("Billing.InvitationRevisionConflict", "Billing membership changed. Reload and try again.");
    public static Error Unavailable => Error.NotFound("Billing.InvitationUnavailable", "This billing invitation is unavailable.");
}
