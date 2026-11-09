using Intelibill.Domain.ValueObjects;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record SaleReturnDto(
    Guid SaleReturnId,
    string ReturnNumber,
    DateTimeOffset ProcessedAt,
    Guid ProcessedBy,
    string? Notes,
    decimal TotalRefundAmount,
    decimal DueReductionAmount,
    decimal PayoutAmount,
    ReturnPayoutDestination? PayoutDestination,
    decimal TotalTaxableAmount,
    decimal TotalTaxAmount,
    SaleReturnCreditNoteSummaryDto? CreditNote,
    IReadOnlyList<SaleReturnItemDto> Items, TenderLine? RefundTender = null, bool? RefundConfirmed = null, TillSettlementEvidence? VoidSettlementEvidence = null);

public sealed record SaleReturnItemDto(
    Guid SaleReturnItemId,
    Guid SaleItemId,
    decimal Quantity,
    SaleReturnCondition? Condition,
    decimal ApprovedRefundAmount,
    decimal TaxableAmount,
    decimal TaxAmount,
    string? Notes, Guid? QuarantineChangeId = null, decimal QuarantinedQuantity = 0, string? DispositionReasonCode = null);

public sealed record SaleReturnCreditNoteSummaryDto(
    Guid CreditNoteId,
    string Code,
    decimal OriginalAmount,
    decimal AvailableBalance,
    DateTimeOffset? ExpiresAt,
    string Reason);

/// <summary>Immutable completion identity saved for keyed return and void recovery.</summary>
public sealed record SaleReturnOperationOutcome(Guid SaleReturnId, Guid SaleId);
