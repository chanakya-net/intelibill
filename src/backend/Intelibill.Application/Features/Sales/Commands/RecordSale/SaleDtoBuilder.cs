using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

public sealed class SaleDtoBuilder(IItemRepository itemRepository)
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
        var itemNameById = items.ToDictionary(i => i.Id, i => i.Name);

        return BuildSaleDto(sale, itemNameById, warnings, creditNoteRedemptions);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Kept instance method so handlers route via DI instance.")]
    internal SaleDto BuildSaleDto(
        Sale sale,
        IReadOnlyDictionary<Guid, string> itemNameById,
        IReadOnlyList<string> warnings)
    {
        return BuildSaleDto(sale, itemNameById, warnings, []);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Kept instance method so handlers route via DI instance.")]
    internal SaleDto BuildSaleDto(
        Sale sale,
        IReadOnlyDictionary<Guid, string> itemNameById,
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
                si.HasPriceMismatch)
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
            }).ToList(),
            warnings)
        {
            CreditNoteRedemptions = creditNoteRedemptions,
        };

        string ResolveLineName(SaleItem saleItem)
        {
            if (!string.IsNullOrWhiteSpace(saleItem.LineName))
            {
                return saleItem.LineName;
            }

            if (saleItem.LineType == SaleLineType.Goods && saleItem.ItemId.HasValue)
            {
                return itemNameById.GetValueOrDefault(saleItem.ItemId.Value, "Unknown Item");
            }

            return "Unknown Service";
        }
    }
}
