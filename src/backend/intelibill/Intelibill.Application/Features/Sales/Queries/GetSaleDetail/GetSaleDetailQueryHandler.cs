using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Queries.GetSaleDetail;

public sealed class GetSaleDetailQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISaleRepository saleRepository,
    ISaleReturnRepository saleReturnRepository,
    IItemRepository itemRepository,
    ICreditNoteRepository creditNoteRepository,
    ISaleCancellationRepository cancellationRepository)
{
    private const string NotReturned = "NotReturned";
    private const string PartiallyReturned = "PartiallyReturned";
    private const string FullyReturned = "FullyReturned";

    [LicenceAction(LicenceAction.History)]
    public async Task<ErrorOr<SaleDto>> Handle(
        GetSaleDetailQuery query,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeAsync(query, cancellationToken);
        if (authorization.IsError)
            return authorization.Errors;

        var sale = await saleRepository.GetByIdAsync(query.SaleId, query.ShopId, cancellationToken);
        if (sale is null)
            return Error.NotFound("Sale.NotFound", $"Sale '{query.SaleId}' was not found.");

        var itemLookups = await LoadItemLookupsAsync(query, sale, cancellationToken);

        var saleReturns = await saleReturnRepository.GetBySaleAsync(query.ShopId, sale.Id, cancellationToken);
        var activeReturns = saleReturns
            .Where(r => !r.IsVoided)
            .OrderBy(r => r.ProcessedAt)
            .ToList();
        var creditNotesByReturnId = await GetCreditNotesByReturnIdAsync(
            activeReturns.Select(r => r.Id).ToList(),
            query.ShopId,
            cancellationToken);
        var creditNoteRedemptions = await creditNoteRepository.GetRedemptionsBySaleIdAsync(
            query.ShopId,
            sale.Id,
            cancellationToken) ?? [];
        var returnedQuantityBySaleItemId = activeReturns
            .SelectMany(r => r.Items)
            .GroupBy(i => i.SaleItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var cancellation = sale.Status == SaleStatus.Cancelled
            ? await cancellationRepository.GetBySaleAsync(query.ShopId, sale.Id, cancellationToken)
            : null;

        return BuildSaleDto(
            sale,
            itemLookups,
            returnedQuantityBySaleItemId,
            activeReturns,
            creditNotesByReturnId,
            creditNoteRedemptions,
            cancellation);
    }

    private async Task<ErrorOr<Success>> AuthorizeAsync(
        GetSaleDetailQuery query,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(query.UserId, cancellationToken);
        if (user is null)
            return Errors.User.NotFound;

        var shop = await shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(query.UserId, query.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        return Result.Success;
    }

    private async Task<ItemLookups> LoadItemLookupsAsync(
        GetSaleDetailQuery query,
        Sale sale,
        CancellationToken cancellationToken)
    {
        var itemIds = sale.Items
            .Where(i => i.LineType == SaleLineType.Goods && i.ItemId.HasValue)
            .Select(i => i.ItemId!.Value)
            .Distinct()
            .ToList();
        var items = await itemRepository.GetByIdsAsync(query.ShopId, itemIds, cancellationToken);
        var itemNameById = items.ToDictionary(i => i.Id, i => i.Name);
        var itemById = items.ToDictionary(i => i.Id);

        return new ItemLookups(itemNameById, itemById);
    }

    private static SaleDto BuildSaleDto(
        Sale sale,
        ItemLookups itemLookups,
        IReadOnlyDictionary<Guid, decimal> returnedQuantityBySaleItemId,
        IReadOnlyList<SaleReturn> activeReturns,
        IReadOnlyDictionary<Guid, SaleReturnCreditNoteSummaryDto?> creditNotesByReturnId,
        IReadOnlyList<CreditNoteRedemptionListRow> creditNoteRedemptions,
        SaleCancellation? cancellation)
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
                .Select(si => MapSaleItem(sale, si, itemLookups, returnedQuantityBySaleItemId))
                .ToList(),
            [])
        {
            Status = sale.Status,
            Version = sale.Version,
            Cancellation = cancellation is null ? null : SaleCancellationDto.From(cancellation),
            Document = sale.InvoiceDocument,
            TillSessionId = sale.TillSessionId,
            TillOverrideReason = sale.TillOverrideReason,
            TenderLines = sale.TenderLines,
            CreditNoteRedemptions = creditNoteRedemptions
                .Select(redemption => new SaleCreditNoteRedemptionSummaryDto(
                    redemption.CreditNoteId,
                    redemption.Code,
                    redemption.Amount))
                .ToList(),
            Returns = activeReturns
                .Select(r => MapSaleReturn(r, creditNotesByReturnId))
                .ToList(),
        };
    }

    private static SaleItemDto MapSaleItem(
        Sale sale,
        SaleItem si,
        ItemLookups itemLookups,
        IReadOnlyDictionary<Guid, decimal> returnedQuantityBySaleItemId)
    {
        return new SaleItemDto(
            si.Id,
            si.LineType,
            si.ItemId,
            si.InventoryBatchId,
            si.ServiceId,
            si.LineCode,
            ResolveLineName(si, itemLookups),
            si.Quantity,
            si.SalesPrice,
            si.TaxRatePercent,
            si.IsPriceIncludingTax,
            si.HasPriceMismatch,
            GetReturnedQuantity(si.Id, returnedQuantityBySaleItemId),
            GetReturnableQuantity(sale, si, returnedQuantityBySaleItemId),
            GetReturnStatus(si, returnedQuantityBySaleItemId))
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
            Uom = si.LineType == SaleLineType.Goods && si.ItemId.HasValue
                ? itemLookups.ItemById.GetValueOrDefault(si.ItemId.Value)?.Uom
                : null,
            QuantityScale = si.LineType == SaleLineType.Service
                ? QuantityUnits.MaxScale
                : QuantityPolicy.ReturnScale(
                    si.ItemId.HasValue ? itemLookups.ItemById.GetValueOrDefault(si.ItemId.Value)?.QuantityScale : null,
                    si.Quantity),
        };
    }

    private static SaleReturnDto MapSaleReturn(
        SaleReturn r,
        IReadOnlyDictionary<Guid, SaleReturnCreditNoteSummaryDto?> creditNotesByReturnId)
    {
        return new SaleReturnDto(
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
            r.Items
                .Select(i => new SaleReturnItemDto(
                    i.Id,
                    i.SaleItemId,
                    i.Quantity,
                    i.Condition,
                    i.ApprovedRefundAmount,
                    i.TaxableAmount,
                    i.TaxAmount,
                    i.Notes,
                    i.QuarantineChangeId,
                    i.QuarantinedQuantity,
                    i.DispositionReasonCode))
                .ToList(),
            r.RefundTender,
            r.RefundConfirmed,
            r.VoidSettlementEvidence);
    }

    private static decimal GetReturnedQuantity(
        Guid saleItemId,
        IReadOnlyDictionary<Guid, decimal> returnedQuantityBySaleItemId)
    {
        return returnedQuantityBySaleItemId.GetValueOrDefault(saleItemId);
    }

    private static decimal GetReturnableQuantity(
        Sale sale,
        SaleItem saleItem,
        IReadOnlyDictionary<Guid, decimal> returnedQuantityBySaleItemId)
    {
        return sale.Status == SaleStatus.Cancelled
            ? 0m
            : Math.Max(0m, saleItem.Quantity - GetReturnedQuantity(saleItem.Id, returnedQuantityBySaleItemId));
    }

    private static string GetReturnStatus(
        SaleItem saleItem,
        IReadOnlyDictionary<Guid, decimal> returnedQuantityBySaleItemId)
    {
        var returnedQuantity = GetReturnedQuantity(saleItem.Id, returnedQuantityBySaleItemId);
        if (returnedQuantity <= 0m)
            return NotReturned;

        return returnedQuantity >= saleItem.Quantity ? FullyReturned : PartiallyReturned;
    }

    private static string ResolveLineName(SaleItem saleItem, ItemLookups itemLookups)
    {
        if (!string.IsNullOrWhiteSpace(saleItem.LineName))
        {
            return saleItem.LineName;
        }

        if (saleItem.LineType == SaleLineType.Goods && saleItem.ItemId.HasValue)
        {
            return itemLookups.ItemNameById.GetValueOrDefault(saleItem.ItemId.Value, "Unknown Item");
        }

        return "Unknown Service";
    }

    private async Task<IReadOnlyDictionary<Guid, SaleReturnCreditNoteSummaryDto?>> GetCreditNotesByReturnIdAsync(
        List<Guid> returnIds,
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

    private sealed record ItemLookups(
        Dictionary<Guid, string> ItemNameById,
        Dictionary<Guid, Item> ItemById);
}
