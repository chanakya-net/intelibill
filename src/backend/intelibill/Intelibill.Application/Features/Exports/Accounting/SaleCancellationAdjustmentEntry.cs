using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Exports.Accounting;

/// <summary>Positive immutable magnitudes. Consumers post their negative revenue/cost effects.</summary>
public sealed record SaleCancellationAdjustmentEntry(
    Guid CancellationId, Guid SaleId, string ReferenceNumber, string InvoiceNumber,
    DateOnly InvoiceBusinessDate, DateOnly BusinessDate, DateTimeOffset PostedAt,
    string BusinessTimeZoneId, string Reason, string PolicyVersion, InvoiceSeller Seller, InvoiceBuyer Buyer,
    decimal TotalAmount, decimal RevenueAmount, decimal RawCostAmount, decimal RecordedCogs,
    decimal DueReductionAmount, decimal CashRefundAmount, IReadOnlyList<SaleCancellationLine> Lines)
{
    public string? CustomerName => Buyer.Name;
    public string Disclaimer => "Commercial cancellation record — not a GST credit note." +
        (CashRefundAmount > 0 ? " Cash refund is recorded from the operator's confirmation." : string.Empty);
}
