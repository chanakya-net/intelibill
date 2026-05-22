using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.OfflineSalesSnapshot.DTOs;

public interface IOfflineSalesSnapshotStreamRecord
{
    string Type { get; }
}

public sealed record OfflineSalesSnapshotMetadata(
    Guid SnapshotId,
    Guid ShopId,
    int SchemaVersion,
    DateTimeOffset StartedAt);

public sealed record OfflineSellableBatchDto(
    Guid BatchId,
    Guid ItemId,
    string ItemName,
    string Barcode,
    string Uom,
    string? HsnCode,
    string BatchNumber,
    decimal Quantity,
    decimal CostPrice,
    decimal Mrp,
    decimal SalesPrice,
    decimal TaxRatePercent,
    bool TaxIncluded,
    bool PurchaseTaxIncluded,
    DateOnly? ExpiryDate);

public sealed record OfflineCustomerLiteDto(
    Guid CustomerId,
    string Name,
    string PhoneNumber);

public sealed record OfflineDiscountRuleDto(
    Guid RuleId,
    DiscountRuleType RuleType,
    string Name,
    string? Description,
    Guid? InventoryBatchId,
    decimal Percentage,
    decimal? ThresholdAmount,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    bool BelowCostConfirmed);

public sealed record OfflineActiveLeaseDto(
    Guid LeaseId,
    Guid InvoiceSequenceId,
    string DeviceId,
    int FiscalYearStart,
    string Prefix,
    int RangeStart,
    int RangeEnd,
    int NextNumber,
    int NumberPadding,
    DateTimeOffset ReservedAt,
    DateTimeOffset ExpiresAt);

public sealed record OfflineSalesSnapshotComplete(
    Guid SnapshotId,
    DateTimeOffset CompletedAt,
    int BatchCount,
    int CustomerCount,
    int DiscountRuleCount,
    int ActiveLeaseCount);

public sealed record OfflineSalesSnapshotError(
    Guid SnapshotId,
    string Code,
    string Message);

public sealed record OfflineSalesSnapshotMetadataRecord(OfflineSalesSnapshotMetadata Metadata) : IOfflineSalesSnapshotStreamRecord
{
    public string Type => "metadata";
}

public sealed record OfflineSalesSnapshotBatchRecord(OfflineSellableBatchDto Batch) : IOfflineSalesSnapshotStreamRecord
{
    public string Type => "batch";
}

public sealed record OfflineSalesSnapshotCustomerRecord(OfflineCustomerLiteDto Customer) : IOfflineSalesSnapshotStreamRecord
{
    public string Type => "customer";
}

public sealed record OfflineSalesSnapshotDiscountRuleRecord(OfflineDiscountRuleDto DiscountRule) : IOfflineSalesSnapshotStreamRecord
{
    public string Type => "discountRule";
}

public sealed record OfflineSalesSnapshotActiveLeaseRecord(OfflineActiveLeaseDto ActiveLease) : IOfflineSalesSnapshotStreamRecord
{
    public string Type => "activeLease";
}

public sealed record OfflineSalesSnapshotCompleteRecord(OfflineSalesSnapshotComplete Complete) : IOfflineSalesSnapshotStreamRecord
{
    public string Type => "complete";
}

public sealed record OfflineSalesSnapshotErrorRecord(OfflineSalesSnapshotError Error) : IOfflineSalesSnapshotStreamRecord
{
    public string Type => "error";
}

