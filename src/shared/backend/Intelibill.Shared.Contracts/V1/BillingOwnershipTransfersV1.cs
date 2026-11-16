using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

public static class BillingOwnershipTransfersV1
{
    public const string Issue = "billing.ownership-transfer.issue";
    public const string Inbox = "billing.ownership-transfer.inbox";
    public const string Preview = "billing.ownership-transfer.preview";
    public const string Accept = "billing.ownership-transfer.accept";
    public const string Recover = "billing.ownership-transfer.recover";
    public const string Policy = "prospective-billing-owner/1";
    public const string Path = MerchantBillingDelegationV1.InternalPrefix + "/ownership-transfers";
    public const string Operations = MerchantBillingDelegationV1.InternalPrefix + "/ownership-transfer-operations";
    public const int PageSize = 30;
    public const int MaximumReasonLength = 500;

    public static string IssuePath(Guid account) => $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/ownership-transfers";
    public static string InboxPath(Guid? after) => after is { } cursor ? $"{Path}/after/{cursor:D}" : Path;
    public static string PreviewPath(Guid proposal) => $"{Path}/{proposal:D}";
    public static string AcceptPath(Guid proposal) => PreviewPath(proposal) + "/accept";
    public static string OperationPath(string action, Guid key) => $"{Operations}/{action}/{key:D}";

    public static bool IsAction(string action) => action is Issue or Inbox or Preview or Accept or Recover;
    public static bool IsOperation(string action) => action is "issue" or "accept";
    public static string? NormalizeReason(string? value) =>
        value?.Trim() is { Length: > 0 and <= MaximumReasonLength } reason && !reason.Any(char.IsControl) ? reason : null;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IssueBillingOwnershipTransferV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("expectedVersion")] int ExpectedVersion,
    [property: JsonPropertyName("recipientActorId")] Guid? RecipientActorId,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("requestSha256")] string RequestSha256,
    [property: JsonPropertyName("confirmTransfer")] bool ConfirmTransfer)
{
    public override string ToString() => "[REDACTED]";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AcceptBillingOwnershipTransferV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("expectedVersion")] int ExpectedVersion,
    [property: JsonPropertyName("responsibilityPolicyVersion")] string ResponsibilityPolicyVersion,
    [property: JsonPropertyName("acceptResponsibility")] bool AcceptResponsibility);

public sealed record BillingOwnershipProposalV1(Guid TransferId, Guid AccountId, string AccountName,
    Guid OwnerActorId, Guid RecipientActorId, int Revision, string ResponsibilityPolicyVersion, DateTimeOffset ExpiresAt);

public sealed record BillingOwnershipInboxV1(BillingOwnershipProposalV1[] Transfers, Guid? NextCursor);
public sealed record BillingOwnershipAcknowledgementV1(Guid OperationKey);
public sealed record BillingOwnershipOutcomeV1(Guid OperationKey, Guid AccountId, Guid TransferId,
    DateTimeOffset EffectiveAt, string Role);
