using System.Text.Json.Serialization;
using Intelibill.Application.Common.Licensing.Offline;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.OfflineSalesSnapshot.DTOs;

public interface IOfflineSalesSnapshotStreamRecord
{
    string Type { get; }
}

public sealed record OfflineSalesSnapshotMetadata(
    Guid SnapshotId,
    Guid ShopId,
    int SchemaVersion,
    DateTimeOffset StartedAt,
    string? BusinessTimeZoneId = null,
    long? CatalogVersion = null);

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
    DateOnly? ExpiryDate,
    int QuantityScale,
    string DocumentUnit,
    Guid? ExpiryEvidenceId = null,
    ExpiryPolicy? EffectivePolicy = null,
    decimal? PhysicalQuantity = null,
    decimal? QuarantinedQuantity = null,
    decimal? AvailableQuantity = null,
    DateTimeOffset? ExpiryCapturedAt = null,
    DateTimeOffset? ExpiryEvidenceExpiresAt = null,
    uint? BatchVersion = null,
    DateOnly? ManufacturingDate = null);

/// <summary>
/// A customer the device can bill offline. GSTIN, state and address are the buyer particulars an
/// issued document carries, so the device's copy matches the one the server issues on sync.
/// </summary>
public sealed record OfflineCustomerLiteDto(
    Guid CustomerId,
    string Name,
    string PhoneNumber,
    string? Gstin = null,
    string? StateCode = null,
    string? Address = null);

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
    DateTimeOffset ExpiresAt,
    InvoiceSellerProfile? SellerProfile = null);

public sealed record OfflineSalesSnapshotComplete(
    Guid SnapshotId,
    DateTimeOffset CompletedAt,
    int BatchCount,
    int CustomerCount,
    int DiscountRuleCount,
    int ActiveLeaseCount,
    int ServiceCount = 0);

/// <summary>
/// A stream that stopped after records were sent. <paramref name="Message"/> is fixed text, never
/// the failure's own; <paramref name="TraceId"/> finds the server's diagnostic for it.
/// </summary>
public sealed record OfflineSalesSnapshotError(
    Guid SnapshotId,
    string Code,
    string Message,
    string TraceId);

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

public sealed record OfflineSellableServiceDto(
    Guid ServiceId,
    string Code,
    string Name,
    decimal Price,
    decimal TaxRatePercent,
    bool TaxIncluded,
    string? HsnCode);

public sealed record OfflineSalesSnapshotServiceRecord(OfflineSellableServiceDto Service) : IOfflineSalesSnapshotStreamRecord
{
    public string Type => "service";
}

/// <summary>
/// Permit evidence attached to a lease-bound snapshot stream, emitted immediately before the
/// completion record. Unbound streams never emit this record.
/// </summary>
public sealed record OfflineSalesSnapshotLicencePermit(
    int Version,
    string Status,
    Guid LeaseId,
    Guid GrantId,
    Guid SnapshotId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? PermitId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? KeyId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SignedPermit = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? NotBefore = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ValidUntil = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LimitingReason = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RefusalCode = null)
{
    public static OfflineSalesSnapshotLicencePermit Issued(
        Guid leaseId, Guid grantId, Guid snapshotId, OfflinePermitEvidence evidence) =>
        new(OfflinePermitContractV1.Version, "issued", leaseId, grantId, snapshotId,
            evidence.PermitId, evidence.KeyId, evidence.SignedPermit,
            evidence.NotBefore, evidence.ValidUntil, evidence.LimitingReason);

    public static OfflineSalesSnapshotLicencePermit Refused(
        Guid leaseId, Guid grantId, Guid snapshotId, string refusalCode) =>
        new(OfflinePermitContractV1.Version, "refused", leaseId, grantId, snapshotId,
            RefusalCode: refusalCode);
}

public sealed record OfflineSalesSnapshotLicencePermitRecord(OfflineSalesSnapshotLicencePermit LicencePermit)
    : IOfflineSalesSnapshotStreamRecord
{
    public string Type => "licencePermit";
}

