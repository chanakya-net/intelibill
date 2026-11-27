using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

public static class CoverageSlotsV1
{
    public const string Read = "billing.slot-assignment.read";
    public const string Request = "billing.slot-assignment.request";
    public const string Recover = "billing.slot-assignment.recover";
    public const string OwnerRead = "billing.slot-assignment.owner-read";
    public const string Kind = "CoverageSlotConsent";
    public const string ReceiptPath = "/internal/licensing/v1/merchant-events/coverage-slot-consent";
    public static string Path(Guid account, Guid term) =>
        $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/subscriptions/{term:D}/slot-assignments";
    public const int PageSize = 50;
    public static string ReadPath(Guid account, Guid term, int page) => page == 1 ? Path(account, term)
        : FormattableString.Invariant($"{Path(account, term)}/pages/{page}");
    public static string OwnerPath(Guid account, Guid shop, Guid assignment) =>
        $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/slot-assignments/{shop:D}/{assignment:D}/owner";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssignUnusedCoverageSlotV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("shopId")] Guid ShopId,
    [property: JsonPropertyName("observedCapacity")] int ObservedCapacity,
    [property: JsonPropertyName("observedPaidThrough")] DateTimeOffset ObservedPaidThrough,
    [property: JsonPropertyName("acknowledgedSharedEnd")] bool AcknowledgedSharedEnd,
    [property: JsonPropertyName("acknowledgedNoNewFee")] bool AcknowledgedNoNewFee);

public sealed record CoverageSlotAssignmentV1(Guid AssignmentId, Guid AccountId, Guid TermId, Guid ShopId,
    Guid AffiliationRequestId, string ShopName, int Capacity, int MemberAllowance, DateTimeOffset CoverageEndsAt,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? AppliedAt, string State, string? Reason,
    string Activation, string MerchantReceipt = "None");

public sealed record SlotShopV1(Guid ShopId, string ShopName);
public sealed record CoverageSlotTermV1(Guid TermId, int Capacity, int UnusedSlots, DateTimeOffset PaidThrough,
    SlotShopV1[] EligibleShops, CoverageSlotAssignmentV1[] Assignments, bool HasMore);
public sealed record CoverageSlotOwnerV1(CoverageSlotAssignmentV1 Assignment, string Status);
public sealed record CoverageSlotConsentV1(int SchemaVersion, Guid EventId, Guid AssignmentId, Guid AccountId, Guid TermId,
    Guid ShopId, Guid AffiliationRequestId, Guid ActorId, Guid MembershipId, long AuthorityVersion, int ActiveMembers,
    string SeatHash, int MemberAllowance, DateTimeOffset CoverageEndsAt, DateTimeOffset AcceptedAt);
