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
        var itemIds = sale.Items
            .Where(i => i.LineType == SaleLineType.Goods && i.ItemId.HasValue)
            .Select(i => i.ItemId!.Value)
            .Distinct()
            .ToList();
        var items = await itemRepository.GetByIdsAsync(sale.ShopId, itemIds, cancellationToken);
        var itemNameById = items.ToDictionary(i => i.Id, i => i.Name);

        return BuildSaleDto(sale, itemNameById, warnings);
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
            sale.Items
                .Where(si => si.LineType == SaleLineType.Goods && si.ItemId.HasValue && si.InventoryBatchId.HasValue)
                .Select(si => new SaleItemDto(
                si.Id,
                si.ItemId!.Value,
                !string.IsNullOrWhiteSpace(si.LineName) ? si.LineName : itemNameById.GetValueOrDefault(si.ItemId!.Value, "Unknown Item"),
                si.InventoryBatchId!.Value,
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
            warnings);
    }
}
