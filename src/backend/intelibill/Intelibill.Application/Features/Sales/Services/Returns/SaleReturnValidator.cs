using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Services.Returns;

internal sealed class SaleReturnValidator(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISaleRepository saleRepository,
    ISaleReturnRepository saleReturnRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository,
    IPaymentReceiptRepository paymentReceiptRepository,
    ISaleReturnCalculator saleReturnCalculator,
    IItemRepository itemRepository,
    IBusinessClock businessClock, Intelibill.Application.Features.Expiry.BatchDispositionService dispositions)
    : ISaleReturnValidator
{
    public async Task<ErrorOr<SaleReturnValidationResult>> ValidateAsync(
        SaleReturnValidationRequest request,
        CancellationToken cancellationToken)
    {
        var context = await LoadContextAsync(request, cancellationToken);
        if (context.IsError)
            return context.Errors;

        var (user, shop, membership, sale) = context.Value;

        var validationErrors = ValidateRequestShape(request);
        if (validationErrors.Count > 0)
            return validationErrors;

        var activeReturns = await LoadActiveReturnsAsync(request, sale, cancellationToken);
        var returnedBySaleItemId = SummariseReturned(activeReturns);
        var saleItemsById = sale.Items.ToDictionary(i => i.Id);
        var itemScaleById = await LoadItemScalesAsync(request.ShopId, sale, cancellationToken);
        var lineInputs = new List<ValidatedSaleReturnLine>();
        var today = businessClock.GetBusinessDate(request.RestorationAt ?? businessClock.UtcNow, shop.BusinessTimeZoneId);

        foreach (var item in request.Items)
        {
            var line = await ValidateLineAsync(
                request,
                item,
                saleItemsById,
                itemScaleById,
                returnedBySaleItemId,
                today,
                cancellationToken);
            if (line.IsError)
            {
                validationErrors.AddRange(line.Errors);
                continue;
            }

            lineInputs.Add(line.Value);
        }

        if (validationErrors.Count > 0)
            return validationErrors;

        var outstandingDue = await GetOutstandingDueAsync(request.ShopId, sale, activeReturns, cancellationToken);
        var calculation = saleReturnCalculator.Calculate(
            BuildCalculationRequest(request, sale, lineInputs, returnedBySaleItemId, outstandingDue));

        return new SaleReturnValidationResult(user, shop, membership, sale, activeReturns, lineInputs, calculation);
    }

    private async Task<ErrorOr<ValidationContext>> LoadContextAsync(
        SaleReturnValidationRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(request.ActorUserId, cancellationToken);
        if (user is null)
            return Errors.User.NotFound;

        var shop = await shopRepository.GetByIdAsync(request.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(request.ActorUserId, request.ShopId, cancellationToken);
        if (membership is null || !membership.IsActive)
            return Errors.Shop.MembershipNotFound;

        var sale = await saleRepository.GetByIdAsync(request.SaleId, request.ShopId, cancellationToken);
        if (sale is null)
            return Errors.Sale.NotFound(request.SaleId);

        if (sale.Status == SaleStatus.Cancelled)
            return Errors.SaleReturn.SaleCancelled;

        if (request.Licence is { } licence && SaleReturnSourcePolicy.Check(licence, sale, businessClock) is { } refusal)
            return refusal;

        return new ValidationContext(user, shop, membership, sale);
    }

    private async Task<List<SaleReturn>> LoadActiveReturnsAsync(
        SaleReturnValidationRequest request,
        Sale sale,
        CancellationToken cancellationToken)
    {
        return (await saleReturnRepository.GetBySaleAsync(request.ShopId, sale.Id, cancellationToken))
            .Where(r => !r.IsVoided)
            .ToList();
    }

    private static Dictionary<Guid, ReturnedShare> SummariseReturned(IReadOnlyList<SaleReturn> activeReturns)
    {
        return activeReturns
            .SelectMany(r => r.Items)
            .GroupBy(i => i.SaleItemId)
            .ToDictionary(
                g => g.Key,
                g => new ReturnedShare(
                    g.Sum(i => i.Quantity),
                    g.Sum(i => i.MaxRefundAmount),
                    g.Sum(i => i.TaxableAmount),
                    g.Sum(i => i.TaxAmount)));
    }

    private async Task<Dictionary<Guid, int>> LoadItemScalesAsync(
        Guid shopId,
        Sale sale,
        CancellationToken cancellationToken)
    {
        var itemIds = sale.Items
            .Where(i => i.LineType == SaleLineType.Goods && i.ItemId.HasValue)
            .Select(i => i.ItemId!.Value)
            .Distinct()
            .ToList();
        return itemIds.Count == 0
            ? new Dictionary<Guid, int>()
            : (await itemRepository.GetByIdsAsync(shopId, itemIds, cancellationToken))
                .ToDictionary(i => i.Id, i => i.QuantityScale);
    }

    /// <summary>Checks one requested line; the first failing rule for the line is the error returned.</summary>
    private async Task<ErrorOr<ValidatedSaleReturnLine>> ValidateLineAsync(
        SaleReturnValidationRequest request,
        SaleReturnValidationLineRequest item,
        Dictionary<Guid, SaleItem> saleItemsById,
        Dictionary<Guid, int> itemScaleById,
        Dictionary<Guid, ReturnedShare> returnedBySaleItemId,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        if (!saleItemsById.TryGetValue(item.SaleItemId, out var saleItem))
            return Errors.Sale.ReturnSaleItemNotFound(item.SaleItemId);

        if (saleItem.LineType != item.LineType)
            return Errors.Sale.ReturnLineTypeMismatch(item.SaleItemId);

        var quantities = CheckQuantities(item, saleItem, itemScaleById, returnedBySaleItemId);
        if (quantities.IsError)
            return quantities.Errors;

        var (returnedQuantity, returnableQuantity) = quantities.Value;

        if (saleItem.LineType == SaleLineType.Service)
        {
            if (item.Condition.HasValue)
                return Errors.Sale.ReturnServiceMustBeRefundOnly(item.SaleItemId);

            return new ValidatedSaleReturnLine(item, saleItem, null, returnedQuantity, returnableQuantity);
        }

        return await ValidateGoodsLineAsync(
            request,
            item,
            saleItem,
            returnedQuantity,
            returnableQuantity,
            today,
            cancellationToken);
    }

    private static ErrorOr<QuantityShare> CheckQuantities(
        SaleReturnValidationLineRequest item,
        SaleItem saleItem,
        Dictionary<Guid, int> itemScaleById,
        Dictionary<Guid, ReturnedShare> returnedBySaleItemId)
    {
        int? itemScale = saleItem.LineType == SaleLineType.Goods
            && saleItem.ItemId.HasValue
            && itemScaleById.TryGetValue(saleItem.ItemId.Value, out var knownScale)
                ? knownScale
                : null;
        var allowedScale = saleItem.LineType == SaleLineType.Service
            ? QuantityUnits.MaxScale
            : QuantityPolicy.ReturnScale(itemScale, saleItem.Quantity);
        if (!QuantityPolicy.FitsScale(item.Quantity, allowedScale))
        {
            return Errors.Sale.ReturnQuantityPrecisionExceeded(
                string.IsNullOrWhiteSpace(saleItem.LineName) ? saleItem.LineCode : saleItem.LineName,
                allowedScale);
        }

        var returnedQuantity = returnedBySaleItemId.GetValueOrDefault(item.SaleItemId)?.Quantity ?? 0m;
        var returnableQuantity = Math.Max(0m, saleItem.Quantity - returnedQuantity);
        if (item.Quantity > returnableQuantity)
            return Errors.Sale.ReturnQuantityExceedsRemaining(item.SaleItemId, returnableQuantity);

        return new QuantityShare(returnedQuantity, returnableQuantity);
    }

    private async Task<ErrorOr<ValidatedSaleReturnLine>> ValidateGoodsLineAsync(
        SaleReturnValidationRequest request,
        SaleReturnValidationLineRequest item,
        SaleItem saleItem,
        decimal returnedQuantity,
        decimal returnableQuantity,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        if (!item.Condition.HasValue)
            return Errors.Sale.ReturnGoodsConditionInvalid(item.SaleItemId);

        if (!saleItem.ItemId.HasValue || !saleItem.InventoryBatchId.HasValue)
            return Errors.Sale.ReturnSaleItemNotFound(item.SaleItemId);

        var batchId = saleItem.InventoryBatchId.Value;
        var batch = await inventoryBatchRepository.GetByIdAsync(batchId, cancellationToken);
        if (batch is null || batch.ShopId != request.ShopId || batch.Id != batchId)
            return Errors.Sale.ReturnBatchNotFound(batchId);

        if (item.Condition == SaleReturnCondition.Restockable && batch.IsVoided)
            return Errors.Sale.ReturnBatchVoided(batch.BatchNumber);

        Intelibill.Application.Features.Expiry.RestorationDisposition? disposition = null;
        if (item.Condition == SaleReturnCondition.Restockable)
        {
            var evaluated = await dispositions.EvaluateAsync(batch, today, item.Quantity, cancellationToken);
            if (evaluated.IsError)
                return evaluated.Errors;

            disposition = evaluated.Value;
        }

        return new ValidatedSaleReturnLine(item, saleItem, batch, returnedQuantity, returnableQuantity, disposition);
    }

    private static SaleReturnCalculationRequest BuildCalculationRequest(
        SaleReturnValidationRequest request,
        Sale sale,
        List<ValidatedSaleReturnLine> lineInputs,
        Dictionary<Guid, ReturnedShare> returnedBySaleItemId,
        decimal outstandingDue)
    {
        return new SaleReturnCalculationRequest(
            lineInputs.Select(line =>
            {
                var returned = returnedBySaleItemId.GetValueOrDefault(line.Request.SaleItemId) ?? ReturnedShare.None;
                return new SaleReturnLineCalculationRequest(
                    line.Request.SaleItemId,
                    line.Request.Quantity,
                    line.SaleItem.CostPrice,
                    line.SaleItem.SalesPrice,
                    line.SaleItem.TaxRatePercent,
                    line.SaleItem.IsPriceIncludingTax,
                    line.SaleItem.Quantity,
                    line.SaleItem.TaxableAmount,
                    line.SaleItem.TaxAmount,
                    line.SaleItem.TotalAmount,
                    line.Request.Condition,
                    line.Request.ApprovedRefundAmount,
                    line.Request.Notes,
                    PreviouslyReturnedQuantity: returned.Quantity,
                    PreviouslyAllocatedTotalAmount: returned.TotalAmount,
                    PreviouslyAllocatedTaxableAmount: returned.TaxableAmount,
                    PreviouslyAllocatedTaxAmount: returned.TaxAmount);
            }).ToList(),
            OutstandingDueAmount: outstandingDue,
            CustomerBalanceBefore: sale.CustomerId.HasValue ? outstandingDue : null,
            request.DueReductionOverrideAmount,
            request.DueOverrideReason);
    }

    private static List<Error> ValidateRequestShape(SaleReturnValidationRequest request)
    {
        var errors = new List<Error>();
        if (request.Items.Count == 0)
            errors.Add(Errors.Sale.ReturnItemsRequired);

        foreach (var item in request.Items)
        {
            if (item.Quantity <= 0m)
                errors.Add(Errors.Sale.ReturnQuantityMustBePositive(item.SaleItemId));
        }

        var duplicateSaleItemIds = request.Items
            .GroupBy(i => i.SaleItemId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var saleItemId in duplicateSaleItemIds)
            errors.Add(Errors.Sale.ReturnDuplicateSaleItem(saleItemId));

        return errors;
    }

    private async Task<decimal> GetOutstandingDueAsync(
        Guid shopId,
        Sale sale,
        IReadOnlyList<SaleReturn> activeReturns,
        CancellationToken cancellationToken)
    {
        if (sale.CustomerId.HasValue)
        {
            var balance = await customerLedgerEntryRepository.GetCustomerBalanceAsync(
                shopId,
                sale.CustomerId.Value,
                cancellationToken);

            // Payments allocated to this invoice already settled part of it; a return can only
            // reduce what is still owed on the invoice itself, and never more than the account owes.
            var allocated = (await paymentReceiptRepository.GetActiveAllocatedByTargetAsync(
                shopId,
                PaymentAllocationTargetType.CustomerInvoice,
                [sale.Id],
                cancellationToken)).GetValueOrDefault(sale.Id);
            var invoiceOutstanding = sale.DueAmount - activeReturns.Sum(r => r.DueReductionAmount) - allocated;
            return Math.Max(0m, Math.Min(balance, invoiceOutstanding));
        }

        return Math.Max(0m, sale.DueAmount - activeReturns.Sum(r => r.DueReductionAmount));
    }

    private sealed record ValidationContext(User User, Shop Shop, ShopMembership Membership, Sale Sale);

    private sealed record QuantityShare(decimal Returned, decimal Returnable);

    /// <summary>What the line's active returns already took back of the sold quantity and its invoice value.</summary>
    private sealed record ReturnedShare(decimal Quantity, decimal TotalAmount, decimal TaxableAmount, decimal TaxAmount)
    {
        public static readonly ReturnedShare None = new(0m, 0m, 0m, 0m);
    }
}
