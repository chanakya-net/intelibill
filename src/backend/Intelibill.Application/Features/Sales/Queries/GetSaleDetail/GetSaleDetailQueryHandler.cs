using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Queries.GetSaleDetail;

public sealed class GetSaleDetailQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISaleRepository saleRepository,
    ISaleReturnRepository saleReturnRepository,
    IItemRepository itemRepository,
    ICreditNoteRepository creditNoteRepository)
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

        var itemIds = sale.Items
            .Where(i => i.LineType == SaleLineType.Goods && i.ItemId.HasValue)
            .Select(i => i.ItemId!.Value)
            .Distinct()
            .ToList();
        var items = await itemRepository.GetByIdsAsync(query.ShopId, itemIds, cancellationToken);
        var itemNameById = items.ToDictionary(i => i.Id, i => i.Name);

        var saleReturns = await saleReturnRepository.GetBySaleAsync(query.ShopId, sale.Id, cancellationToken);
        var activeReturns = saleReturns
            .Where(r => !r.IsVoided)
            .OrderBy(r => r.ProcessedAt)
            .ToList();
        var creditNotesByReturnId = await GetCreditNotesByReturnIdAsync(
            activeReturns.Select(r => r.Id).ToList(),
            query.ShopId,
            cancellationToken);
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
                GetReturnedQuantity(si.Id),
                GetReturnableQuantity(si),
                GetReturnStatus(si))
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
                r.PayoutDestination,
                r.TotalTaxableAmount,
                r.TotalTaxAmount,
                creditNotesByReturnId.GetValueOrDefault(r.Id),
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

        decimal GetReturnableQuantity(SaleItem saleItem)
        {
            return Math.Max(0m, saleItem.Quantity - GetReturnedQuantity(saleItem.Id));
        }

        string GetReturnStatus(SaleItem saleItem)
        {
            var returnedQuantity = GetReturnedQuantity(saleItem.Id);
            if (returnedQuantity <= 0m)
                return NotReturned;

            return returnedQuantity >= saleItem.Quantity ? FullyReturned : PartiallyReturned;
        }

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

        async Task<IReadOnlyDictionary<Guid, SaleReturnCreditNoteSummaryDto?>> GetCreditNotesByReturnIdAsync(
            IReadOnlyList<Guid> returnIds,
            Guid shopId,
            CancellationToken ct)
        {
            if (returnIds.Count == 0)
            {
                return new Dictionary<Guid, SaleReturnCreditNoteSummaryDto?>();
            }

            var notes = await creditNoteRepository.GetByReturnIdsAsync(shopId, returnIds, ct);
            var noteByReturnId = notes
                .GroupBy(note => note.SaleReturnId)
                .ToDictionary(group => group.Key, group => group.First());

            return returnIds.ToDictionary(
                returnId => returnId,
                returnId => noteByReturnId.TryGetValue(returnId, out var creditNote)
                    ? new SaleReturnCreditNoteSummaryDto(
                        creditNote.Id,
                        creditNote.Code,
                        creditNote.OriginalAmount,
                        creditNote.AvailableBalance,
                        creditNote.ExpiresAt,
                        creditNote.Reason)
                    : null);
        }
    }
}
