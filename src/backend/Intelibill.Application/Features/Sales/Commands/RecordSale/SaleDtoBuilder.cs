using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

public sealed class SaleDtoBuilder(IItemRepository itemRepository, ISaleCancellationRepository cancellationRepository)
{
    public async Task<SaleDto> BuildSaleDtoAsync(
        Sale sale,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        return await BuildSaleDtoAsync(sale, warnings, [], cancellationToken);
    }

    public async Task<SaleDto> BuildSaleDtoAsync(
        Sale sale,
        IReadOnlyList<string> warnings,
        IReadOnlyList<SaleCreditNoteRedemptionSummaryDto> creditNoteRedemptions,
        CancellationToken cancellationToken)
    {
        var itemIds = sale.Items
            .Where(i => i.LineType == SaleLineType.Goods && i.ItemId.HasValue)
            .Select(i => i.ItemId!.Value)
            .Distinct()
            .ToList();
        var items = await itemRepository.GetByIdsAsync(sale.ShopId, itemIds, cancellationToken);
        var itemById = items.ToDictionary(i => i.Id);

        var cancellation = sale.Status == SaleStatus.Cancelled
            ? await cancellationRepository.GetBySaleAsync(sale.ShopId, sale.Id, cancellationToken)
            : null;
        return BuildSaleDto(sale, itemById, warnings, creditNoteRedemptions) with
        {
            Cancellation = cancellation is null ? null : SaleCancellationDto.From(cancellation),
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Kept instance method so handlers route via DI instance.")]
    internal SaleDto BuildSaleDto(
        Sale sale,
        IReadOnlyDictionary<Guid, Item> itemById,
        IReadOnlyList<string> warnings)
    {
        return BuildSaleDto(sale, itemById, warnings, []);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Kept instance method so handlers route via DI instance.")]
    internal SaleDto BuildSaleDto(
        Sale sale,
        IReadOnlyDictionary<Guid, Item> itemById,
        IReadOnlyList<string> warnings,
        IReadOnlyList<SaleCreditNoteRedemptionSummaryDto> creditNoteRedemptions)
    {
        return new SaleDto(
            sale.Id,
            sale.InvoiceNumber,
            sale.CustomerId,
            sale.CustomerName,
            sale.CustomerPhone,
            sale.PaymentMethod,
            sale.SoldAt,
            sale.PaidAmount,
            sale.DueAmount,
            sale.TotalBeforeDiscount,
            sale.TotalDiscountAmount,
            sale.TotalAmount,
            sale.TotalTaxAmount,
            sale.CreditNoteAppliedAmount,
            sale.Items
                .Select(si => new SaleItemDto(
                si.Id,
                si.LineType,
                si.ItemId,
                si.InventoryBatchId,
                si.ServiceId,
                si.LineCode,
                ResolveLineName(si),
                si.Quantity,
                si.SalesPrice,
                si.TaxRatePercent,
                si.IsPriceIncludingTax,
                si.HasPriceMismatch,
                returnableQuantity: sale.Status == SaleStatus.Cancelled ? 0m : si.Quantity)
            {
                OriginalSalesPrice = si.OriginalSalesPrice,
                FinalSalesPrice = si.FinalSalesPrice,
                PreTaxAmountBeforeDiscount = si.PreTaxAmountBeforeDiscount,
                ItemDiscountAmount = si.ItemDiscountAmount,
                SaleDiscountAmount = si.SaleDiscountAmount,
                TaxableAmount = si.TaxableAmount,
                TaxAmount = si.TaxAmount,
                TotalAmount = si.TotalAmount,
                HsnCode = si.HsnCode,
                SavingsAmount = si.ItemDiscountAmount + si.SaleDiscountAmount,
                Uom = GoodsItemOf(si)?.Uom,
                QuantityScale = si.LineType == SaleLineType.Service
                    ? QuantityUnits.MaxScale
                    : QuantityPolicy.ReturnScale(GoodsItemOf(si)?.QuantityScale, si.Quantity),
            }).ToList(),
            warnings)
        {
            CreditNoteRedemptions = creditNoteRedemptions,
            Status = sale.Status,
            Version = sale.Version,
            Document = sale.InvoiceDocument,
            TillSessionId = sale.TillSessionId,
            TillOverrideReason = sale.TillOverrideReason,
            TenderLines = sale.TenderLines,
        };

        Item? GoodsItemOf(SaleItem saleItem) =>
            saleItem.LineType == SaleLineType.Goods && saleItem.ItemId.HasValue
                ? itemById.GetValueOrDefault(saleItem.ItemId.Value)
                : null;

        string ResolveLineName(SaleItem saleItem)
        {
            if (!string.IsNullOrWhiteSpace(saleItem.LineName))
            {
                return saleItem.LineName;
            }

            if (saleItem.LineType == SaleLineType.Goods && saleItem.ItemId.HasValue)
            {
                return itemById.TryGetValue(saleItem.ItemId.Value, out var item) ? item.Name : "Unknown Item";
            }

            return "Unknown Service";
        }
    }
}
