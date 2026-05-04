using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Services.Returns;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Queries.PreviewSaleReturn;

public sealed class PreviewSaleReturnQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISaleRepository saleRepository,
    ISaleReturnRepository saleReturnRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    ISaleReturnCalculator saleReturnCalculator)
{
    public async Task<ErrorOr<SaleReturnPreviewDto>> Handle(
        PreviewSaleReturnQuery query,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(query.ActorUserId, cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", "User not found.");

        var shop = await shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(query.ActorUserId, query.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var sale = await saleRepository.GetByIdAsync(query.SaleId, query.ShopId, cancellationToken);
        if (sale is null)
            return Errors.Sale.NotFound(query.SaleId);

        var validationErrors = ValidateRequestShape(query);
        if (validationErrors.Count > 0)
            return validationErrors;

        var activeReturns = (await saleReturnRepository.GetBySaleAsync(query.ShopId, sale.Id, cancellationToken))
            .Where(r => !r.IsVoided)
            .ToList();

        var returnedQuantityBySaleItemId = activeReturns
            .SelectMany(r => r.Items)
            .GroupBy(i => i.SaleItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var saleItemsById = sale.Items.ToDictionary(i => i.Id);
        var lineInputs = new List<ValidatedPreviewLine>();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

        foreach (var item in query.Items)
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

            var batch = await inventoryBatchRepository.GetByIdAsync(saleItem.InventoryBatchId, cancellationToken);
            if (batch is null || batch.ShopId != query.ShopId || batch.Id != saleItem.InventoryBatchId)
            {
                validationErrors.Add(Errors.Sale.ReturnBatchNotFound(saleItem.InventoryBatchId));
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

            lineInputs.Add(new ValidatedPreviewLine(item, saleItem, returnedQuantity, returnableQuantity));
        }

        if (validationErrors.Count > 0)
            return validationErrors;

        var calculation = saleReturnCalculator.Calculate(new SaleReturnCalculationRequest(
            lineInputs.Select(line => new SaleReturnLineCalculationRequest(
                line.Request.SaleItemId,
                line.Request.Quantity,
                line.SaleItem.CostPrice,
                line.SaleItem.SalesPrice,
                line.SaleItem.TaxRatePercent,
                line.SaleItem.IsPriceIncludingTax,
                line.Request.Condition,
                line.Request.ApprovedRefundAmount,
                line.Request.Notes)).ToList(),
            OutstandingDueAmount: GetOutstandingDue(sale, activeReturns),
            CustomerBalanceBefore: sale.CustomerId.HasValue ? GetOutstandingDue(sale, activeReturns) : null,
            query.DueReductionOverrideAmount,
            query.DueOverrideReason));

        var hasFinancialAccess = membership.Role is ShopRole.Owner or ShopRole.Manager;
        var calculationsBySaleItemId = calculation.Lines.ToDictionary(line => line.SaleItemId);

        return new SaleReturnPreviewDto(
            sale.Id,
            hasFinancialAccess,
            lineInputs.Select(line =>
            {
                var calculated = calculationsBySaleItemId[line.Request.SaleItemId];
                return new SaleReturnPreviewLineDto(
                    line.Request.SaleItemId,
                    line.SaleItem.ItemId,
                    line.SaleItem.InventoryBatchId,
                    line.Request.Quantity,
                    line.ReturnedQuantity,
                    line.ReturnableQuantity,
                    line.Request.Condition,
                    line.Request.Condition == SaleReturnCondition.Restockable,
                    hasFinancialAccess ? new SaleReturnPreviewLineFinancialDto(
                        calculated.OriginalCostPrice,
                        calculated.OriginalSalesPrice,
                        calculated.OriginalTaxRatePercent,
                        calculated.OriginalIsPriceIncludingTax,
                        calculated.MaxRefundAmount,
                        calculated.ApprovedRefundAmount,
                        calculated.TaxableAmount,
                        calculated.TaxAmount) : null);
            }).ToList(),
            hasFinancialAccess ? new SaleReturnPreviewFinancialDto(
                calculation.TotalRefundAmount,
                calculation.DueReductionAmount,
                calculation.PayoutAmount,
                calculation.TotalTaxableAmount,
                calculation.TotalTaxAmount,
                calculation.CustomerBalanceBefore,
                calculation.CustomerBalanceAfter) : null,
            FilterWarnings(calculation.Warnings, hasFinancialAccess));
    }

    private static List<Error> ValidateRequestShape(PreviewSaleReturnQuery query)
    {
        var errors = new List<Error>();
        if (query.Items.Count == 0)
            errors.Add(Errors.Sale.ReturnItemsRequired);

        foreach (var item in query.Items)
        {
            if (item.Quantity <= 0m)
                errors.Add(Errors.Sale.ReturnQuantityMustBePositive(item.SaleItemId));
        }

        var duplicateSaleItemIds = query.Items
            .GroupBy(i => i.SaleItemId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var saleItemId in duplicateSaleItemIds)
            errors.Add(Errors.Sale.ReturnDuplicateSaleItem(saleItemId));

        return errors;
    }

    private static decimal GetOutstandingDue(Sale sale, IReadOnlyList<SaleReturn> activeReturns) =>
        Math.Max(0m, sale.DueAmount - activeReturns.Sum(r => r.DueReductionAmount));

    private static List<SaleReturnPreviewWarningDto> FilterWarnings(
        IReadOnlyList<SaleReturnCalculationWarning> warnings,
        bool hasFinancialAccess)
    {
        var visibleWarnings = hasFinancialAccess
            ? warnings
            : warnings.Where(w => w.Code == "sale_return.note_required.wastage");

        return visibleWarnings
            .Select(w => new SaleReturnPreviewWarningDto(w.Code, w.Message, w.Severity.ToString()))
            .ToList();
    }

    private sealed record ValidatedPreviewLine(
        PreviewSaleReturnItemQuery Request,
        SaleItem SaleItem,
        decimal ReturnedQuantity,
        decimal ReturnableQuantity);
}
