using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

public interface ISaleDtoBuilder
{
    Task<SaleDto> BuildSaleDtoAsync(Sale sale, IReadOnlyList<string> warnings, CancellationToken cancellationToken);
}

internal sealed class SaleDtoBuilder(IItemRepository itemRepository)
    : ISaleDtoBuilder
{
    Task<SaleDto> ISaleDtoBuilder.BuildSaleDtoAsync(
        Sale sale,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken) =>
        BuildSaleDtoAsync(sale, warnings, cancellationToken);

    internal async Task<SaleDto> BuildSaleDtoAsync(
        Sale sale,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        var itemIds = sale.Items.Select(i => i.ItemId).Distinct().ToList();
        var items = await itemRepository.GetByIdsAsync(sale.ShopId, itemIds, cancellationToken);
        var itemNameById = items.ToDictionary(i => i.Id, i => i.Name);

        return BuildSaleDto(sale, itemNameById, warnings);
    }

    internal static SaleDto BuildSaleDto(
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
            sale.Items.Select(si => new SaleItemDto(
                si.Id,
                si.ItemId,
                itemNameById.GetValueOrDefault(si.ItemId, "Unknown Item"),
                si.InventoryBatchId,
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
