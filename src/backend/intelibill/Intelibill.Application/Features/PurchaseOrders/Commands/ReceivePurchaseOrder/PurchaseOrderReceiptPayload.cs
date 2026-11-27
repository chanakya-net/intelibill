using Intelibill.Application.Common.Operations;

namespace Intelibill.Application.Features.PurchaseOrders.Commands.ReceivePurchaseOrder;

/// <summary>Canonical business content only; absent timestamps and client IDs stay absent.</summary>
public sealed record PurchaseOrderReceiptPayload(
    Guid PurchaseOrderId,
    string? ReferenceNumber,
    string? Notes,
    DateTimeOffset? ReceivedAt,
    IReadOnlyList<ReceivePurchaseOrderLineInput> Lines)
{
    public static PurchaseOrderReceiptPayload From(ReceivePurchaseOrderCommand command) => new(
        command.PurchaseOrderId,
        NormalizeText(command.ReferenceNumber),
        NormalizeText(command.Notes),
        command.ReceivedAt?.ToUniversalTime(),
        command.Lines.Select(line => line with
        {
            Barcode = line.Barcode?.Trim() ?? string.Empty,
            BatchNumber = line.BatchNumber?.Trim() ?? string.Empty,
            TotalPurchaseCost = FinancialOperationHasher.NormalizeAmount(line.TotalPurchaseCost),
            Mrp = FinancialOperationHasher.NormalizeAmount(line.Mrp),
            SalesPrice = FinancialOperationHasher.NormalizeAmount(line.SalesPrice),
            TaxRatePercent = FinancialOperationHasher.NormalizeAmount(line.TaxRatePercent),
        }).ToList());

    private static string? NormalizeText(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
