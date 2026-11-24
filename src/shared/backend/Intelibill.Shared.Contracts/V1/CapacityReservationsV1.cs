namespace Intelibill.Shared.Contracts.V1;

public sealed record CapacityReservationV1(int SchemaVersion, Guid EventId, string Phase, Guid OrderId, Guid AccountId,
    Guid TermId, Guid RoutingShopId, string BindingHash, int ShopCount, string SourceHash, int PriorCapacity,
    int PriorMembers, int Capacity, int Members, DateTimeOffset StartsAt, DateTimeOffset PaidThrough,
    DateTimeOffset ExpiresAt, long SourceRevision, long TargetRevision, string? ReservationDigest)
{
    public const string Reserve = "CapacityReserve";
    public const string Consume = "CapacityConsume";
    public const string ReservePath = "/internal/licensing/v1/capacity-reservations/reserve";
    public const string ConsumePath = "/internal/licensing/v1/capacity-reservations/consume";
    public const string ReceiptPath = "/internal/licensing/v1/merchant-events/capacity-reservation";
    public const string Receipt = "CapacityReceipt";

    public bool IsValid(string phase) => SchemaVersion == 1 && Phase == phase && EventId != Guid.Empty &&
        AccountId != Guid.Empty && OrderId != Guid.Empty && TermId != Guid.Empty && RoutingShopId != Guid.Empty &&
        SourceRevision is >= 0 and < long.MaxValue && TargetRevision == SourceRevision + 1 &&
        ShopCount is > 0 && ShopCount <= PriorCapacity && Capacity >= PriorCapacity && Capacity <= CheckoutOrdersV1.MaxShops &&
        PriorMembers > 0 && Members >= PriorMembers && Members <= CheckoutOrdersV1.MaxShops &&
        (Capacity > PriorCapacity || Members > PriorMembers) && EntitlementSnapshotsV1.IsDigest(BindingHash) &&
        EntitlementSnapshotsV1.IsDigest(SourceHash) && EntitlementSnapshotsV1.IsInstant(StartsAt) &&
        EntitlementSnapshotsV1.IsInstant(PaidThrough) && EntitlementSnapshotsV1.IsInstant(ExpiresAt) &&
        StartsAt < ExpiresAt && ExpiresAt <= PaidThrough &&
        (phase == Reserve ? ReservationDigest is null : phase == Consume && EntitlementSnapshotsV1.IsDigest(ReservationDigest));
}

public sealed record CapacityReceiptV1(int SchemaVersion, Guid EventId, Guid OrderId, Guid AccountId, Guid TermId,
    Guid RoutingShopId, string Phase, long TargetRevision, string CommandDigest, string Outcome, string? Reason);
