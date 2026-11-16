using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Billing;

public sealed record MerchantBillingInvitation(Guid OperationKey, int ExpectedOwnerRevision, string? RecipientKind, string? Recipient)
{
    public override string ToString() => "[REDACTED]";
}

public interface IMerchantBillingInvitations
{
    Task<ErrorOr<BillingInvitationAcknowledgementV1>> IssueAsync(MerchantBillingCaller caller, Guid accountId,
        MerchantBillingInvitation request, CancellationToken cancellationToken);
    Task<ErrorOr<BillingInvitationInboxV1>> InboxAsync(MerchantBillingCaller caller, Guid? after, CancellationToken cancellationToken);
    Task<ErrorOr<BillingRosterV1>> RosterAsync(MerchantBillingCaller caller, Guid accountId, Guid? after, CancellationToken cancellationToken);
    Task<ErrorOr<BillingManagerRemovalV1>> RemoveManagerAsync(MerchantBillingCaller caller, Guid accountId,
        Guid memberId, RemoveBillingManagerV1 request, CancellationToken cancellationToken);
    Task<ErrorOr<BillingAccountV1>> AcceptAsync(MerchantBillingCaller caller, Guid invitationId, int revision,
        CancellationToken cancellationToken);
}

public static class MerchantBillingInvitationErrors
{
    public static Error InvalidRecipient => Error.Validation("Billing.InvitationRecipientInvalid", "Enter a valid email or phone number.");
    public static Error StaleRevision => Error.Conflict("Billing.InvitationRevisionConflict", "Billing membership changed. Reload and try again.");
    public static Error UnavailableInvitation => Error.NotFound("Billing.InvitationUnavailable", "This billing invitation is unavailable.");
}
