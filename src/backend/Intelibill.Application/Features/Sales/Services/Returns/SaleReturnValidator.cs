using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Services.Returns;

internal sealed class SaleReturnValidator(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISaleRepository saleRepository,
    ISaleReturnRepository saleReturnRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository,
    ISaleReturnCalculator saleReturnCalculator)
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

        var validationErrors = ValidateRequestShape(request);
        if (validationErrors.Count > 0)
            return validationErrors;

        var activeReturns = (await saleReturnRepository.GetBySaleAsync(request.ShopId, sale.Id, cancellationToken))
            .Where(r => !r.IsVoided)
            .ToList();

        var returnedQuantityBySaleItemId = activeReturns
            .SelectMany(r => r.Items)
            .GroupBy(i => i.SaleItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var saleItemsById = sale.Items.ToDictionary(i => i.Id);
        var lineInputs = new List<ValidatedSaleReturnLine>();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

        foreach (var item in request.Items)
        {
            if (!saleItemsById.TryGetValue(item.SaleItemId, out var saleItem))
            {
                validationErrors.Add(Errors.Sale.ReturnSaleItemNotFound(item.SaleItemId));
                continue;
            }

            var returnedQuantity = returnedQuantityBySaleItemId.GetValueOrDefault(item.SaleItemId);
            var returnableQuantity = Math.Max(0m, saleItem.Quantity - returnedQuantity);
            if (item.Quantity > returnableQuantity)
            {
                validationErrors.Add(Errors.Sale.ReturnQuantityExceedsRemaining(item.SaleItemId, returnableQuantity));
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
            lineInputs.Select(line => new SaleReturnLineCalculationRequest(
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
                line.Request.Notes)).ToList(),
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
            return Math.Max(0m, balance);
        }

        return Math.Max(0m, sale.DueAmount - activeReturns.Sum(r => r.DueReductionAmount));
    }
}
