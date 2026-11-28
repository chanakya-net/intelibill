using ErrorOr;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Application.Common.Errors;

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
    public static Error InvalidRecipient => Errors.Billing.InvitationRecipientInvalid;
    public static Error StaleRevision => Errors.Billing.InvitationRevisionConflict;
    public static Error UnavailableInvitation => Errors.Billing.InvitationUnavailable;
}
