using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Queries.GetSaleDetail;

public sealed class GetSaleDetailQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISaleRepository saleRepository,
    IItemRepository itemRepository)
{
    public async Task<ErrorOr<SaleDto>> Handle(
        GetSaleDetailQuery query,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(query.UserId, cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", "User not found.");

        var shop = await shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(query.UserId, query.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var sale = await saleRepository.GetByIdAsync(query.SaleId, query.ShopId, cancellationToken);
        if (sale is null)
            return Error.NotFound("Sale.NotFound", $"Sale '{query.SaleId}' was not found.");

        var itemIds = sale.Items.Select(i => i.ItemId).Distinct().ToList();
        var items = await itemRepository.GetByIdsAsync(query.ShopId, itemIds, cancellationToken);
        var itemNameById = items.ToDictionary(i => i.Id, i => i.Name);

        return new SaleDto(
            sale.Id,
            sale.InvoiceNumber,
            sale.CustomerId,
            sale.PaymentMethod,
            sale.SoldAt,
            sale.PaidAmount,
            sale.DueAmount,
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
                si.HasPriceMismatch)).ToList(),
            []);
    }
}
