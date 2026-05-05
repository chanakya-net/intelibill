using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Queries.GetProfitLossReport;

public sealed class GetProfitLossReportQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISaleRepository saleRepository,
    ISaleReturnRepository saleReturnRepository,
    IInventoryAdjustmentRepository inventoryAdjustmentRepository)
{
    public async Task<ErrorOr<IReadOnlyList<ProfitLossReportItemDto>>> Handle(
        GetProfitLossReportQuery query,
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

        var sales = await saleRepository.GetByShopAsync(query.ShopId, cancellationToken);

        var report = new List<ProfitLossReportItemDto>();

        foreach (var sale in sales)
        {
            decimal totalCost = 0;
            decimal revenueExclTax = 0;
            decimal revenueInclTax = 0;

            foreach (var item in sale.Items)
            {
                decimal basePrice = item.IsPriceIncludingTax
                    ? item.SalesPrice / (1 + item.TaxRatePercent / 100)
                    : item.SalesPrice;

                decimal taxPerUnit = basePrice * (item.TaxRatePercent / 100);
                decimal finalPrice = basePrice + taxPerUnit;

                totalCost += item.CostPrice * item.Quantity;
                revenueExclTax += basePrice * item.Quantity;
                revenueInclTax += finalPrice * item.Quantity;
            }

            report.Add(new ProfitLossReportItemDto(
                sale.Id,
                sale.InvoiceNumber,
                sale.SoldAt,
                sale.CustomerName,
                totalCost,
                WastageCost: 0m,
                revenueExclTax,
                revenueInclTax,
                revenueInclTax - totalCost,
                revenueExclTax - totalCost,
                ProfitLossReportRowTypes.Sale,
                InventoryAdjustmentId: null));

            var saleReturns = await saleReturnRepository.GetBySaleAsync(query.ShopId, sale.Id, cancellationToken) ?? [];
            foreach (var saleReturn in saleReturns.Where(r => !r.IsVoided))
            {
                var restockableCost = saleReturn.Items
                    .Where(i => i.Condition == SaleReturnCondition.Restockable)
                    .Sum(i => i.OriginalCostPrice * i.Quantity);
                var wastageCost = saleReturn.Items
                    .Where(i => i.Condition == SaleReturnCondition.Wastage)
                    .Sum(i => i.OriginalCostPrice * i.Quantity);
                var returnCostImpact = -restockableCost;
                var approvedRefundTax = saleReturn.Items.Sum(CalculateApprovedRefundTax);
                var returnRevenueInclTax = -saleReturn.TotalRefundAmount;
                var returnRevenueExclTax = -(saleReturn.TotalRefundAmount - approvedRefundTax);

                report.Add(new ProfitLossReportItemDto(
                    sale.Id,
                    $"{sale.InvoiceNumber} / {saleReturn.ReturnNumber}",
                    saleReturn.ProcessedAt,
                    sale.CustomerName,
                    returnCostImpact,
                    wastageCost,
                    returnRevenueExclTax,
                    returnRevenueInclTax,
                    returnRevenueInclTax - returnCostImpact,
                    returnRevenueExclTax - returnCostImpact,
                    ProfitLossReportRowTypes.SaleReturn,
                    InventoryAdjustmentId: null));
            }
        }

        var adjustments = await inventoryAdjustmentRepository.GetProfitLossAdjustmentsAsync(query.ShopId, cancellationToken);
        foreach (var adjustment in adjustments.Where(a => a.Direction == InventoryAdjustmentDirection.Decrease && !a.IsVoided))
        {
            report.Add(new ProfitLossReportItemDto(
                SaleId: null,
                adjustment.AdjustmentNumber,
                adjustment.PerformedAt,
                PartyName: null,
                TotalCost: 0m,
                WastageCost: adjustment.CostImpact,
                RevenueBeforeTax: 0m,
                RevenueAfterTax: 0m,
                ProfitBeforeTax: -adjustment.CostImpact,
                ProfitAfterTax: -adjustment.CostImpact,
                ProfitLossReportRowTypes.InventoryAdjustment,
                adjustment.Id));
        }

        return report
            .OrderByDescending(s => s.OccurredAt)
            .ToList();
    }

    private static decimal CalculateApprovedRefundTax(Domain.Entities.SaleReturnItem item)
    {
        if (item.ApprovedRefundAmount <= 0m || item.MaxRefundAmount <= 0m || item.TaxAmount <= 0m)
        {
            return 0m;
        }

        var tax = item.ApprovedRefundAmount * item.TaxAmount / item.MaxRefundAmount;
        return Math.Round(tax, 2, MidpointRounding.AwayFromZero);
    }
}
