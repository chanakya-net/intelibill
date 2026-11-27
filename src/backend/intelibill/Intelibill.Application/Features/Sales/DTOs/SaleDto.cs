using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record SaleDto(
    Guid SaleId,
    string InvoiceNumber,
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    PaymentMethod PaymentMethod,
    DateTimeOffset SoldAt,
    decimal PaidAmount,
    decimal DueAmount,
    decimal TotalBeforeDiscount,
    decimal TotalDiscountAmount,
    decimal TotalAmount,
    decimal TotalTaxAmount,
    decimal CreditNoteAppliedAmount,
    IReadOnlyList<SaleItemDto> Items,
    IReadOnlyList<string> Warnings)
{
    public IReadOnlyList<SaleExpiryDecision>? ExpiryDecisions { get; init; }
    public Guid? TillSessionId { get; init; }
    public string? TillOverrideReason { get; init; }
    public IReadOnlyList<TenderLine>? TenderLines { get; init; }
    public SaleStatus Status { get; init; }
    public uint Version { get; init; }
    public SaleCancellationDto? Cancellation { get; init; }

    public IReadOnlyList<SaleReturnDto> Returns { get; init; } = [];
    public IReadOnlyList<SaleCreditNoteRedemptionSummaryDto> CreditNoteRedemptions { get; init; } = [];

    /// <summary>The document the sale was issued as; null for sales issued before documents were recorded.</summary>
    public IssuedInvoiceSnapshot? Document { get; init; }

    public bool IsLegacyDocument => Document is null;
}

public sealed record SaleCreditNoteRedemptionSummaryDto(
    Guid CreditNoteId,
    string Code,
    decimal AppliedAmount);
