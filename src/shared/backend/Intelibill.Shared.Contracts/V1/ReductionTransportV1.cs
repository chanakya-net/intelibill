using System.Security.Cryptography;
using System.Text;

namespace Intelibill.Shared.Contracts.V1;

/// <summary>
/// Signed internal service-to-service contracts for live merchant seat observations and renewal-boundary
/// reduction reservations. Every message is bounded within the shared 8,192-byte entitlement transport limit.
/// </summary>
public static class ReductionTransportV1
{
    public const string ObservePath = "/internal/merchant/v1/capacity-reductions/observe";
    public const string ManifestKind = "ReductionManifest";
    public const string ReserveKind = "ReductionReserve";
    public const string ConsumeKind = "ReductionConsume";
    public const string ReceiptKind = "ReductionReceipt";
    public const string ManifestPath = "/internal/licensing/v1/capacity-reductions/manifest";
    public const string ReservePath = "/internal/licensing/v1/capacity-reductions/reserve";
    public const string ConsumePath = "/internal/licensing/v1/capacity-reductions/consume";
    public const string ReceiptPath = "/internal/licensing/v1/merchant-events/capacity-reduction";
    public const string ProofHeader = ShopClosuresV1.ProofHeader;
    public const int ObservePageSize = 25;
    public const int ManifestPageSize = 25;
    public const int BodyLimit = EntitlementSnapshotsV1.BodyLimit;

    public static string BindingDigest(IEnumerable<(Guid ShopId, Guid AffiliationRequestId)> shops) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(
            string.Join(',', shops.OrderBy(s => s.ShopId).Select(s => $"{s.ShopId:D}/{s.AffiliationRequestId:D}")))));

    public static string ObservationDigest(IEnumerable<(Guid ShopId, Guid AffiliationRequestId, int ActiveMembers, string SeatHash)> shops) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(
            string.Join(',', shops.OrderBy(s => s.ShopId)
                .Select(s => $"{s.ShopId:D}/{s.AffiliationRequestId:D}/{s.ActiveMembers}/{s.SeatHash}")))));
}

public sealed record ReductionExpectedShopV1(Guid ShopId, Guid AffiliationRequestId);

public sealed record ReductionObservationRequestV1(
    int SchemaVersion, Guid AccountId, Guid TermId, Guid Challenge, int PageIndex, int PageCount,
    DateTimeOffset ExpiresAt, ReductionExpectedShopV1[] Shops)
{
    public bool IsValid() => SchemaVersion == 1 && AccountId != Guid.Empty && TermId != Guid.Empty &&
        Challenge != Guid.Empty && PageCount > 0 && PageIndex >= 0 && PageIndex < PageCount &&
        EntitlementSnapshotsV1.IsInstant(ExpiresAt) &&
        Shops is { Length: > 0 and <= ReductionTransportV1.ObservePageSize } &&
        Shops.All(s => s.ShopId != Guid.Empty && s.AffiliationRequestId != Guid.Empty) &&
        Shops.Select(s => s.ShopId).Distinct().Count() == Shops.Length;
}

public sealed record ReductionObservedShopV1(
    Guid ShopId, Guid AffiliationRequestId, int ActiveMembers, string SeatHash, bool AffiliationMatched);

public sealed record ReductionObservationResponseV1(
    int SchemaVersion, Guid AccountId, Guid TermId, Guid Challenge, int PageIndex, int PageCount,
    DateTimeOffset ObservedAt, ReductionObservedShopV1[] Shops);

public sealed record ReductionManifestShopV1(
    Guid ShopId, Guid AffiliationRequestId, bool Retained, int ExpectedActiveMembers, string ExpectedSeatHash);

public sealed record ReductionManifestPageV1(
    int SchemaVersion, Guid EventId, Guid ScheduleId, Guid OrderId, Guid AccountId, Guid TermId, Guid RoutingShopId,
    long Generation, int PageIndex, int PageCount, int TotalSourceShops, int RetainedShopCount,
    string SourceBindingDigest, string RetainedBindingDigest, DateTimeOffset ExpiresAt,
    ReductionManifestShopV1[] Shops)
{
    public bool IsValid() => SchemaVersion == 1 && EventId != Guid.Empty && ScheduleId != Guid.Empty &&
        OrderId != Guid.Empty && AccountId != Guid.Empty && TermId != Guid.Empty && RoutingShopId != Guid.Empty &&
        Generation > 0 && PageCount > 0 && PageIndex >= 0 && PageIndex < PageCount &&
        TotalSourceShops is > 0 and <= CheckoutOrdersV1.MaxShops &&
        RetainedShopCount is > 0 && RetainedShopCount <= TotalSourceShops &&
        EntitlementSnapshotsV1.IsDigest(SourceBindingDigest) && EntitlementSnapshotsV1.IsDigest(RetainedBindingDigest) &&
        EntitlementSnapshotsV1.IsInstant(ExpiresAt) &&
        Shops is { Length: > 0 and <= ReductionTransportV1.ManifestPageSize } &&
        Shops.All(s => s.ShopId != Guid.Empty && s.AffiliationRequestId != Guid.Empty &&
            s.ExpectedActiveMembers >= 0 && EntitlementSnapshotsV1.IsDigest(s.ExpectedSeatHash)) &&
        Shops.Select(s => s.ShopId).Distinct().Count() == Shops.Length;
}

public sealed record ReductionReservationV1(
    int SchemaVersion, Guid EventId, string Phase, Guid ScheduleId, Guid OrderId, Guid AccountId, Guid TermId,
    Guid RoutingShopId, long Generation, int PageCount, int TotalSourceShops, int RetainedShopCount,
    string SourceBindingDigest, string RetainedBindingDigest, string SourceHash, int PriorCapacity, int PriorMembers,
    int TargetCapacity, int TargetMembers, DateTimeOffset PriorStartsAt, DateTimeOffset PriorPaidThrough,
    DateTimeOffset SuccessorEndsAt, DateTimeOffset ExpiresAt, long SourceRevision, Guid SuccessorTermId,
    bool SuspensionDisclosed, string? ReservationDigest)
{
    public bool IsValid(string phase) => SchemaVersion == 1 && Phase == phase && EventId != Guid.Empty &&
        ScheduleId != Guid.Empty && OrderId != Guid.Empty && AccountId != Guid.Empty && TermId != Guid.Empty &&
        RoutingShopId != Guid.Empty && SuccessorTermId != Guid.Empty && Generation > 0 && PageCount > 0 &&
        SourceRevision is >= 0 and < long.MaxValue && TotalSourceShops is > 0 and <= CheckoutOrdersV1.MaxShops &&
        RetainedShopCount is > 0 && RetainedShopCount <= TargetCapacity && RetainedShopCount <= TotalSourceShops &&
        PriorCapacity is > 0 and <= CheckoutOrdersV1.MaxShops && TargetCapacity is > 0 && TargetCapacity <= PriorCapacity &&
        PriorMembers is > 0 and <= CheckoutOrdersV1.MaxShops && TargetMembers is > 0 && TargetMembers <= PriorMembers &&
        (TargetCapacity < PriorCapacity || TargetMembers < PriorMembers) &&
        EntitlementSnapshotsV1.IsDigest(SourceBindingDigest) && EntitlementSnapshotsV1.IsDigest(RetainedBindingDigest) &&
        EntitlementSnapshotsV1.IsDigest(SourceHash) && EntitlementSnapshotsV1.IsInstant(PriorStartsAt) &&
        EntitlementSnapshotsV1.IsInstant(PriorPaidThrough) && EntitlementSnapshotsV1.IsInstant(SuccessorEndsAt) &&
        EntitlementSnapshotsV1.IsInstant(ExpiresAt) && PriorStartsAt < PriorPaidThrough &&
        PriorPaidThrough < SuccessorEndsAt && PriorPaidThrough < ExpiresAt &&
        (phase == ReductionTransportV1.ReserveKind
            ? ReservationDigest is null
            : phase == ReductionTransportV1.ConsumeKind && EntitlementSnapshotsV1.IsDigest(ReservationDigest));
}

public sealed record ReductionReceiptConflictV1(Guid ShopId, string Reason, int ActiveMembers, int TargetAllowance);

public sealed record ReductionReceiptV1(
    int SchemaVersion, Guid EventId, Guid ScheduleId, Guid OrderId, Guid AccountId, Guid TermId, Guid RoutingShopId,
    string Phase, long Generation, string CommandDigest, string Outcome, string? Reason,
    ReductionReceiptConflictV1[]? Conflicts = null);
