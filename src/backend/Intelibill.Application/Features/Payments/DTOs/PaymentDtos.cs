using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Payments.DTOs;

/// <summary>One requested allocation: settle <paramref name="Amount"/> of the document <paramref name="TargetId"/>.</summary>
public sealed record PaymentAllocationRequest(Guid TargetId, decimal Amount);

/// <summary>
/// A customer credit sale's balance. <see cref="OriginalDueAmount"/> is the due printed at issue
/// and never changes; <see cref="OutstandingAmount"/> is what is still owed on it today.
/// </summary>
public sealed record CustomerInvoiceBalanceDto(
    Guid SaleId,
    string InvoiceNumber,
    DateTimeOffset SoldAt,
    decimal OriginalDueAmount,
    decimal AllocatedPaymentAmount,
    decimal ReturnCreditAmount,
    decimal OutstandingAmount,
    DateOnly? DueDate,
    decimal CancellationCreditAmount = 0m);

/// <summary>A supplier purchase receipt's payable and how much of it payments have settled.</summary>
public sealed record SupplierReceiptBalanceDto(
    Guid PurchaseReceiptValueId,
    Guid InventoryBatchId,
    string BatchNumber,
    string ItemName,
    DateTimeOffset ReceivedAt,
    decimal PayableAmount,
    decimal AllocatedPaymentAmount,
    decimal OutstandingAmount,
    decimal ReturnCreditAmount = 0m,
    decimal NetPayableAmount = 0m,
    decimal SupplierCreditAmount = 0m);

public sealed record PaymentAllocationDto(
    Guid AllocationId,
    PaymentAllocationTargetType TargetType,
    Guid TargetId,
    string? TargetLabel,
    decimal Amount,
    DateTimeOffset AllocatedAt,
    Guid AllocatedBy);

public sealed record PaymentReversalDto(
    Guid ReversalId,
    decimal Amount,
    string Reason,
    DateOnly BusinessDate,
    DateTimeOffset ReversedAt,
    Guid ReversedBy,
    string? ReversedByName,
    Guid LedgerEntryId);

/// <summary>A payment receipt with everything a printed receipt or an audit needs.</summary>
public sealed record PaymentReceiptDto(
    Guid ReceiptId,
    PaymentPartyType PartyType,
    Guid PartyId,
    string? PartyName,
    decimal Amount,
    ReceiptTenderMethod Method,
    Guid? BankAccountId,
    string? ExternalReference,
    DateOnly BusinessDate,
    DateTimeOffset ReceivedAt,
    string? Notes,
    bool IsLegacy,
    Guid CreatedBy,
    string? CreatedByName,
    Guid LedgerEntryId,
    Guid? ExpenseId,
    decimal AllocatedAmount,
    decimal UnallocatedAmount,
    IReadOnlyList<PaymentAllocationDto> Allocations,
    PaymentReversalDto? Reversal);

/// <summary>A receipt line in an account view.</summary>
public sealed record PaymentReceiptSummaryDto(
    Guid ReceiptId,
    decimal Amount,
    ReceiptTenderMethod Method,
    string? ExternalReference,
    DateOnly BusinessDate,
    bool IsLegacy,
    bool IsReversed,
    decimal AllocatedAmount,
    decimal UnallocatedAmount,
    Guid LedgerEntryId);

public sealed record PartyBalancesDto(
    PaymentPartyType PartyType,
    Guid PartyId,
    decimal UnallocatedCredit,
    IReadOnlyList<CustomerInvoiceBalanceDto> Invoices,
    IReadOnlyList<SupplierReceiptBalanceDto> PurchaseReceipts,
    IReadOnlyList<PaymentReceiptSummaryDto> Receipts);
