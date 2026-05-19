using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Queries.GetSaleDetail;

public sealed class GetSaleDetailQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISaleRepository saleRepository,
    ISaleReturnRepository saleReturnRepository,
    IItemRepository itemRepository)
{
    private const string NotReturned = "NotReturned";
    private const string PartiallyReturned = "PartiallyReturned";
    private const string FullyReturned = "FullyReturned";

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

        var saleReturns = await saleReturnRepository.GetBySaleAsync(query.ShopId, sale.Id, cancellationToken);
        var activeReturns = saleReturns
            .Where(r => !r.IsVoided)
            .OrderBy(r => r.ProcessedAt)
            .ToList();
        var returnedQuantityBySaleItemId = activeReturns
            .SelectMany(r => r.Items)
            .GroupBy(i => i.SaleItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

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
                si.HasPriceMismatch,
                GetReturnedQuantity(si.Id),
                GetReturnableQuantity(si.Id, si.Quantity),
                GetReturnStatus(si.Id, si.Quantity))
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
            [])
        {
            Returns = activeReturns.Select(r => new SaleReturnDto(
                r.Id,
                r.ReturnNumber,
                r.ProcessedAt,
                r.ProcessedBy,
                r.Notes,
                r.TotalRefundAmount,
                r.DueReductionAmount,
                r.PayoutAmount,
                r.TotalTaxableAmount,
                r.TotalTaxAmount,
                r.Items.Select(i => new SaleReturnItemDto(
                    i.Id,
                    i.SaleItemId,
                    i.Quantity,
                    i.Condition,
                    i.ApprovedRefundAmount,
                    i.TaxableAmount,
                    i.TaxAmount,
                    i.Notes)).ToList())).ToList(),
        };

        decimal GetReturnedQuantity(Guid saleItemId) =>
            returnedQuantityBySaleItemId.GetValueOrDefault(saleItemId);

        decimal GetReturnableQuantity(Guid saleItemId, decimal soldQuantity) =>
            Math.Max(0m, soldQuantity - GetReturnedQuantity(saleItemId));

        string GetReturnStatus(Guid saleItemId, decimal soldQuantity)
        {
            var returnedQuantity = GetReturnedQuantity(saleItemId);
            if (returnedQuantity <= 0m)
                return NotReturned;

            return returnedQuantity >= soldQuantity ? FullyReturned : PartiallyReturned;
        }
    }
}
