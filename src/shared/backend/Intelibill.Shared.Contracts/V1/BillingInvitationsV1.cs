using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

public static class BillingInvitationsV1
{
    public const string OwnerCheck = "billing.invitation.owner-check";
    public const string Issue = "billing.invitation.issue";
    public const string Inbox = "billing.invitation.inbox";
    public const string Proof = "billing.invitation.proof";
    public const string Accept = "billing.invitation.accept";
    public const string Roster = "billing.member.list";

    public const string InboxPath = MerchantBillingDelegationV1.InternalPrefix + "/invitations";
    public const string AccountRoute = MerchantBillingDelegationV1.AccountsPath + "/{accountId:guid}";
    public const int MaximumRequestBytes = 2048;
    public const int MaximumResponseBytes = 100_000;
    public const int PageSize = 100;
    public const int TokenLength = 86;

    public static string AccountInvitations(Guid accountId) => $"{MerchantBillingDelegationV1.AccountsPath}/{accountId:D}/invitations";
    public static string Members(Guid accountId, Guid? after) => after is { } cursor
        ? $"{MerchantBillingDelegationV1.AccountsPath}/{accountId:D}/members/after/{cursor:D}"
        : $"{MerchantBillingDelegationV1.AccountsPath}/{accountId:D}/members";
    public static string Invitation(Guid invitationId, string action) => $"{InboxPath}/{invitationId:D}/{action}";
    public static string InboxPage(Guid? after) => after is { } cursor ? $"{InboxPath}/after/{cursor:D}" : InboxPath;
}

public sealed record IssueBillingInvitationV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("ownerRevision")] int OwnerRevision,
    [property: JsonPropertyName("invitedActorId")] Guid? InvitedActorId,
    [property: JsonPropertyName("requestSha256")] string RequestSha256);

public sealed record BillingInvitationAcknowledgementV1(Guid OperationKey);
public sealed record BillingOwnerRevisionV1(int OwnerRevision);

public sealed record BillingInvitationV1(Guid InvitationId, Guid AccountId, string AccountName,
    int Revision, DateTimeOffset ExpiresAt);

public sealed record BillingInvitationInboxV1(BillingInvitationV1[] Invitations, Guid? NextCursor);

public sealed record BillingInvitationProofV1(string Token, int Revision)
{
    public override string ToString() => "[REDACTED]";
}

public sealed record AcceptBillingInvitationV1(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("expectedInvitationRevision")] int ExpectedInvitationRevision)
{
    public override string ToString() => "[REDACTED]";
}

public sealed record BillingMemberV1(Guid MemberId, string Role, int Revision, bool IsCurrent);
public sealed record BillingRosterV1(string AccountName, BillingMemberV1[] Members, Guid? NextCursor, int? OwnerRevision);
