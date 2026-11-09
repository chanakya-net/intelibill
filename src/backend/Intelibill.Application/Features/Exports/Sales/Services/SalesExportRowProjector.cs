using Intelibill.Application.Features.Exports.Accounting;
using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Exports.Sales.Services;

public static class SalesExportRowProjector
{
    public static SalesExportSummaryRowDto BuildSummaryRow(Sale sale, IReadOnlyList<ReturnAdjustmentEntry> adjustments)
    {
        var returnAmount = adjustments.Sum(a => a.FinancialAdjustment);
        var storeCredit = adjustments
            .SelectMany(a => a.Settlements)
            .Where(s => s.Kind == ReturnSettlementKind.StoreCreditIssued)
            .ToList();
        var storeCreditCodes = storeCredit
            .Select(s => s.CreditNoteCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct()
            .ToList();

        return new SalesExportSummaryRowDto(
            sale.InvoiceNumber,
            sale.SoldAt,
            sale.BusinessDate,
            sale.InvoiceDocument?.Buyer.Name ?? sale.CustomerName,
            sale.PaymentMethod.ToString(),
            sale.PaidAmount,
            sale.DueAmount,
            sale.TotalBeforeDiscount,
            sale.TotalDiscountAmount,
            sale.Items.Sum(i => i.TaxableAmount),
            sale.TotalTaxAmount,
            sale.TotalAmount,
            adjustments.Count == 0 ? null : string.Join(", ", adjustments.Select(a => a.Label)),
            returnAmount,
            adjustments.Sum(a => a.TaxableAdjustment ?? 0m),
            adjustments.Sum(a => a.TaxAdjustment ?? 0m),
            sale.TotalAmount - returnAmount,
            adjustments.Count > 0,
            sale.Items.Count,
            sale.CreditNoteAppliedAmount,
            storeCreditCodes.Count == 0 ? null : string.Join(", ", storeCreditCodes),
            storeCredit.Sum(s => s.Amount),
            sale.InvoiceDocument?.Totals.Components);
    }

    public static SalesExportLineItemRowDto BuildLineItemRow(
        Sale sale,
        SaleItem saleItem,
        IReadOnlyList<SaleReturn> activeReturns,
        IReadOnlyDictionary<Guid, string> itemNameById)
    {
        var returnedQuantity = activeReturns
            .SelectMany(r => r.Items)
            .Where(ri => ri.SaleItemId == saleItem.Id)
            .Sum(ri => ri.Quantity);
        var itemReturnNumbers = string.Join(", ", activeReturns
            .Where(r => r.Items.Any(ri => ri.SaleItemId == saleItem.Id))
            .Select(r => r.ReturnNumber));
        return BuildLineItemRow(sale, saleItem, returnedQuantity, itemReturnNumbers, itemNameById);
    }

    public static SalesExportLineItemRowDto BuildLineItemRow(
        Sale sale, SaleItem saleItem, decimal returnedQuantity, string? itemReturnNumbers,
        IReadOnlyDictionary<Guid, string> itemNameById)
    {
        var lineName = !string.IsNullOrWhiteSpace(saleItem.LineName)
            ? saleItem.LineName
            : saleItem.ItemId.HasValue
                ? itemNameById.GetValueOrDefault(saleItem.ItemId.Value, "Unknown Item")
                : "Unknown Line";

        return new SalesExportLineItemRowDto(
            sale.InvoiceNumber,
            sale.SoldAt,
            sale.BusinessDate,
            sale.InvoiceDocument?.Buyer.Name ?? sale.CustomerName,
            lineName,
            saleItem.Quantity,
            saleItem.SalesPrice,
            saleItem.ItemDiscountAmount,
            saleItem.SaleDiscountAmount,
            saleItem.TaxRatePercent,
            saleItem.TaxableAmount,
            saleItem.TaxAmount,
            saleItem.TotalAmount,
            saleItem.IsPriceIncludingTax,
            returnedQuantity,
            GetReturnStatus(returnedQuantity, saleItem.Quantity),
            string.IsNullOrWhiteSpace(itemReturnNumbers) ? null : itemReturnNumbers,
            saleItem.LineType);
    }

    private static string GetReturnStatus(decimal returnedQuantity, decimal soldQuantity) =>
        returnedQuantity <= 0 ? "NotReturned" : returnedQuantity >= soldQuantity ? "FullyReturned" : "PartiallyReturned";
}
