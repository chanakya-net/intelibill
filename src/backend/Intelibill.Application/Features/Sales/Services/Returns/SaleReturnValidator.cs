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
    IBusinessClock businessClock)
    : ISaleReturnValidator
{
    public async Task<ErrorOr<SaleReturnValidationResult>> ValidateAsync(
        SaleReturnValidationRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(request.ActorUserId, cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", "User not found.");

        var shop = await shopRepository.GetByIdAsync(request.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(request.ActorUserId, request.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var sale = await saleRepository.GetByIdAsync(request.SaleId, request.ShopId, cancellationToken);
        if (sale is null)
            return Errors.Sale.NotFound(request.SaleId);

        if (sale.Status == SaleStatus.Cancelled)
            return Error.Conflict("SaleReturn.SaleCancelled", "A cancelled sale cannot be returned.");

        var validationErrors = ValidateRequestShape(request);
        if (validationErrors.Count > 0)
            return validationErrors;

        var activeReturns = (await saleReturnRepository.GetBySaleAsync(request.ShopId, sale.Id, cancellationToken))
            .Where(r => !r.IsVoided)
            .ToList();

        var returnedBySaleItemId = activeReturns
            .SelectMany(r => r.Items)
            .GroupBy(i => i.SaleItemId)
            .ToDictionary(
                g => g.Key,
                g => new ReturnedShare(
                    g.Sum(i => i.Quantity),
                    g.Sum(i => i.MaxRefundAmount),
                    g.Sum(i => i.TaxableAmount),
                    g.Sum(i => i.TaxAmount)));

        var saleItemsById = sale.Items.ToDictionary(i => i.Id);
        var itemIds = sale.Items
            .Where(i => i.LineType == SaleLineType.Goods && i.ItemId.HasValue)
            .Select(i => i.ItemId!.Value)
            .Distinct()
            .ToList();
        var itemScaleById = itemIds.Count == 0
            ? new Dictionary<Guid, int>()
            : (await itemRepository.GetByIdsAsync(request.ShopId, itemIds, cancellationToken))
                .ToDictionary(i => i.Id, i => i.QuantityScale);
        var lineInputs = new List<ValidatedSaleReturnLine>();
        var today = businessClock.Today(shop.BusinessTimeZoneId);

        foreach (var item in request.Items)
        {
            if (!saleItemsById.TryGetValue(item.SaleItemId, out var saleItem))
            {
                validationErrors.Add(Errors.Sale.ReturnSaleItemNotFound(item.SaleItemId));
                continue;
            }

            if (saleItem.LineType != item.LineType)
            {
                validationErrors.Add(Errors.Sale.ReturnLineTypeMismatch(item.SaleItemId));
                continue;
            }

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
                validationErrors.Add(Errors.Sale.ReturnQuantityPrecisionExceeded(
                    string.IsNullOrWhiteSpace(saleItem.LineName) ? saleItem.LineCode : saleItem.LineName,
                    allowedScale));
                continue;
            }

            var returnedQuantity = returnedBySaleItemId.GetValueOrDefault(item.SaleItemId)?.Quantity ?? 0m;
            var returnableQuantity = Math.Max(0m, saleItem.Quantity - returnedQuantity);
            if (item.Quantity > returnableQuantity)
            {
                validationErrors.Add(Errors.Sale.ReturnQuantityExceedsRemaining(item.SaleItemId, returnableQuantity));
                continue;
            }

            if (saleItem.LineType == SaleLineType.Service)
            {
                if (item.Condition.HasValue)
                {
                    validationErrors.Add(Errors.Sale.ReturnServiceMustBeRefundOnly(item.SaleItemId));
                    continue;
                }

                lineInputs.Add(new ValidatedSaleReturnLine(item, saleItem, null, returnedQuantity, returnableQuantity));
                continue;
            }

            if (!item.Condition.HasValue)
            {
                validationErrors.Add(Errors.Sale.ReturnGoodsConditionInvalid(item.SaleItemId));
                continue;
            }

            if (!saleItem.ItemId.HasValue || !saleItem.InventoryBatchId.HasValue)
            {
                validationErrors.Add(Errors.Sale.ReturnSaleItemNotFound(item.SaleItemId));
                continue;
            }

            var batchId = saleItem.InventoryBatchId.Value;
            var batch = await inventoryBatchRepository.GetByIdAsync(batchId, cancellationToken);
            if (batch is null || batch.ShopId != request.ShopId || batch.Id != batchId)
            {
                validationErrors.Add(Errors.Sale.ReturnBatchNotFound(batchId));
                continue;
            }

            if (item.Condition == SaleReturnCondition.Restockable && batch.IsVoided)
            {
                validationErrors.Add(Errors.Sale.ReturnBatchVoided(batch.BatchNumber));
                continue;
            }

            if (item.Condition == SaleReturnCondition.Restockable && batch.ExpiryDate.HasValue && batch.ExpiryDate.Value < today)
            {
                validationErrors.Add(Errors.Sale.ReturnBatchExpired(batch.BatchNumber));
                continue;
            }

            lineInputs.Add(new ValidatedSaleReturnLine(item, saleItem, batch, returnedQuantity, returnableQuantity));
        }

        if (validationErrors.Count > 0)
            return validationErrors;

        var outstandingDue = await GetOutstandingDueAsync(request.ShopId, sale, activeReturns, cancellationToken);
        var calculation = saleReturnCalculator.Calculate(new SaleReturnCalculationRequest(
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
            request.DueOverrideReason));

        return new SaleReturnValidationResult(user, shop, membership, sale, activeReturns, lineInputs, calculation);
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

    /// <summary>What the line's active returns already took back of the sold quantity and its invoice value.</summary>
    private sealed record ReturnedShare(decimal Quantity, decimal TotalAmount, decimal TaxableAmount, decimal TaxAmount)
    {
        public static readonly ReturnedShare None = new(0m, 0m, 0m, 0m);
    }
}
