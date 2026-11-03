namespace Intelibill.Application.Features.OfflineReconciliation.DTOs;

// Every enum in these DTOs is carried as its name, so the console never depends on stored numbers.

/// <summary>One page of reconciliation cases, newest first, with the caller's unresolved total.</summary>
public sealed record ReconciliationCasePageDto(
    IReadOnlyList<ReconciliationCaseSummaryDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int UnresolvedCount);

/// <summary>A case as listed: enough to pick it from the queue.</summary>
public sealed record ReconciliationCaseSummaryDto(
    Guid CaseId,
    uint Version,
    string? InvoiceNumber,
    string ClientSaleId,
    string DeviceId,
    string IssueType,
    string Status,
    IReadOnlyList<string> ReasonCodes,
    string Message,
    Guid? SaleId,
    string? Resolution,
    DateTimeOffset CreatedAt,
    ReconciliationPersonDto CreatedBy,
    DateTimeOffset? ResolvedAt);

/// <summary>A case with its evidence, the actions the caller may take and its audit trail.</summary>
public sealed record ReconciliationCaseDto(
    Guid CaseId,
    uint Version,
    string? InvoiceNumber,
    string ClientSaleId,
    string DeviceId,
    string IssueType,
    string Status,
    IReadOnlyList<string> ReasonCodes,
    string Message,
    Guid? ItemId,
    Guid? InventoryBatchId,
    decimal? PrintedQuantity,
    decimal? AvailableQuantity,
    decimal? ConsumedQuantity,
    decimal? UnresolvedQuantity,
    Guid? SaleId,
    string? Resolution,
    string? ResolutionReason,
    Guid? LinkedOperationId,
    Guid? ResolutionStockTransactionId,
    DateTimeOffset CreatedAt,
    ReconciliationPersonDto CreatedBy,
    DateTimeOffset? ResolvedAt,
    ReconciliationPersonDto? ResolvedBy,
    bool HasOriginalEnvelope,
    OfflineSubmissionDto? Submission,
    IReadOnlyList<string> AvailableActions,
    IReadOnlyList<ReconciliationCaseEventDto> Events);

/// <summary>
/// A person named on a case. <see cref="DisplayName"/> is filled only for someone who holds or held a
/// membership in the case's shop; for any other id (for example an issuer the client merely asserted)
/// it is <see cref="UnknownDisplayName"/>, the empty string, which the client renders as its own
/// translated "unknown user" text. Nothing else about such an id is disclosed.
/// </summary>
public sealed record ReconciliationPersonDto(Guid UserId, string DisplayName)
{
    public const string UnknownDisplayName = "";
}

/// <summary>The envelope the case was filed for: who sent it, who issued it and the frozen invoice.</summary>
public sealed record OfflineSubmissionDto(
    Guid SubmissionId,
    string Source,
    DateTimeOffset ClaimedSoldAt,
    DateTimeOffset ReceivedAt,
    string PayloadHash,
    Guid? OriginalLeaseId,
    ReconciliationPersonDto SubmittedBy,
    ReconciliationPersonDto? AssertedIssuer,
    ReconciliationPersonDto? OriginalIssuer,
    string? RecoveryOperationId,
    OfflineInvoiceSnapshotDto Invoice);

/// <summary>The invoice exactly as the device froze it; read-only evidence, never the posted sale.</summary>
public sealed record OfflineInvoiceSnapshotDto(
    string InvoiceNumber,
    DateTimeOffset SoldAt,
    string? CustomerName,
    string? CustomerPhone,
    string PaymentMethod,
    decimal SubtotalBeforeDiscount,
    decimal TotalDiscountAmount,
    decimal TotalTaxAmount,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal DueAmount,
    IReadOnlyList<OfflineInvoiceSnapshotLineDto> Lines);

public sealed record OfflineInvoiceSnapshotLineDto(
    string ItemName,
    string Barcode,
    string? BatchNumber,
    decimal Quantity,
    decimal SalesPrice,
    decimal TaxRatePercent,
    decimal TaxAmount,
    decimal TotalAmount);

public sealed record ReconciliationCaseEventDto(
    string Action,
    string FromStatus,
    string ToStatus,
    ReconciliationPersonDto Actor,
    string Reason,
    Guid? LinkedOperationId,
    DateTimeOffset OccurredAt);
