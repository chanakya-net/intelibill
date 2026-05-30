using System.Globalization;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Queries.GetProfitLossReport;

public sealed class ProfitLossReportBuilder(
    ISaleRepository saleRepository,
    ISaleReturnRepository saleReturnRepository,
    IInventoryAdjustmentRepository inventoryAdjustmentRepository)
{
    public async Task<ProfitLossReportBuildResult> BuildAsync(
        Guid shopId,
        DateOnly from,
        DateOnly to,
        string? type,
        string? search,
        CancellationToken cancellationToken)
    {
        var normalizedType = NormalizeType(type);

        var salesInRange = await saleRepository.GetByShopAndDateRangeAsync(shopId, from, to, cancellationToken);
        var saleReturnsInRange = await saleReturnRepository.GetByShopAndDateRangeAsync(shopId, from, to, cancellationToken);
        var adjustmentsInRange = await inventoryAdjustmentRepository.GetByShopAndDateRangeAsync(shopId, from, to, cancellationToken);

        var additionalSaleIds = saleReturnsInRange
            .Select(r => r.SaleId)
            .Except(salesInRange.Select(s => s.Id))
            .Distinct()
            .ToArray();
        var additionalSales = additionalSaleIds.Length == 0
            ? []
            : await saleRepository.GetByIdsAsync(shopId, additionalSaleIds, cancellationToken);

        var salesById = salesInRange
            .Concat(additionalSales)
            .GroupBy(s => s.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var report = new List<ProfitLossReportItemDto>();

        foreach (var sale in salesInRange)
        {
            decimal saleTotalCost = 0;
            decimal revenueExclTax = 0;
            decimal revenueInclTax = 0;

            foreach (var item in sale.Items)
            {
                if (item.LineType != SaleLineType.Service)
                {
                    saleTotalCost += item.CostPrice * item.Quantity;
                }

                revenueExclTax += item.TaxableAmount;
                revenueInclTax += item.TotalAmount;
            }

            report.Add(new ProfitLossReportItemDto(
                sale.Id,
                sale.InvoiceNumber,
                sale.SoldAt,
                sale.CustomerName,
                saleTotalCost,
                WastageCost: 0m,
                revenueExclTax,
                revenueInclTax,
                revenueInclTax - saleTotalCost,
                revenueExclTax - saleTotalCost,
                CalculateMarginPercent(revenueExclTax - saleTotalCost, saleTotalCost),
                ProfitLossReportRowTypes.Sale,
                InventoryAdjustmentId: null));
        }

        foreach (var saleReturn in saleReturnsInRange.Where(r => !r.IsVoided))
        {
            if (!salesById.TryGetValue(saleReturn.SaleId, out var sale))
            {
                continue;
            }

            var restockableCost = saleReturn.Items
                .Where(i => i.Condition == SaleReturnCondition.Restockable)
                .Sum(i =>
                {
                    var saleItem = sale.Items.FirstOrDefault(si => si.Id == i.SaleItemId);
                    if (saleItem?.LineType == SaleLineType.Service) return 0m;
                    return i.OriginalCostPrice * i.Quantity;
                });
            var wastageCost = saleReturn.Items
                .Where(i => i.Condition == SaleReturnCondition.Wastage)
                .Sum(i =>
                {
                    var saleItem = sale.Items.FirstOrDefault(si => si.Id == i.SaleItemId);
                    if (saleItem?.LineType == SaleLineType.Service) return 0m;
                    return i.OriginalCostPrice * i.Quantity;
                });
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
                CalculateMarginPercent(returnRevenueExclTax - returnCostImpact, returnCostImpact),
                ProfitLossReportRowTypes.SaleReturn,
                InventoryAdjustmentId: null));
        }

        foreach (var adjustment in adjustmentsInRange.Where(a => a.Direction == InventoryAdjustmentDirection.Decrease && !a.IsVoided))
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
                MarginPercent: null,
                RowType: ProfitLossReportRowTypes.InventoryAdjustment,
                InventoryAdjustmentId: adjustment.Id));
        }

        var filtered = report
            .Where(row => MatchesType(row.RowType, normalizedType))
            .Where(row => MatchesSearch(row, search))
            .OrderByDescending(row => row.OccurredAt)
            .ThenByDescending(row => row.ReferenceNumber)
            .ToList();

        var totalCost = filtered.Sum(row => row.TotalCost);
        var revenueIncludingTax = filtered.Sum(row => row.RevenueAfterTax);
        var netProfitAfterTax = filtered.Sum(row => row.ProfitAfterTax);
        var summary = new ProfitLossSummaryDto(
            NetProfitAfterTax: netProfitAfterTax,
            RevenueIncludingTax: revenueIncludingTax,
            TotalCost: totalCost,
            AverageMarginPercent: totalCost == 0m
                ? null
                : Math.Round(netProfitAfterTax * 100m / totalCost, 2, MidpointRounding.AwayFromZero),
            InvoiceCount: filtered.Count(row => row.RowType == ProfitLossReportRowTypes.Sale),
            ReturnCount: filtered.Count(row => row.RowType == ProfitLossReportRowTypes.SaleReturn),
            AdjustmentCount: filtered.Count(row => row.RowType == ProfitLossReportRowTypes.InventoryAdjustment));

        return new ProfitLossReportBuildResult(filtered, summary, from, to, normalizedType, search);
    }

    private static decimal CalculateApprovedRefundTax(SaleReturnItem item)
    {
        if (item.ApprovedRefundAmount <= 0m || item.MaxRefundAmount <= 0m || item.TaxAmount <= 0m)
        {
            return 0m;
        }

        var tax = item.ApprovedRefundAmount * item.TaxAmount / item.MaxRefundAmount;
        return Math.Round(tax, 2, MidpointRounding.AwayFromZero);
    }

    private static bool MatchesType(string rowType, string type) =>
        type == "all" || string.Equals(rowType, type, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesSearch(ProfitLossReportItemDto row, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var term = search.Trim();
        var numericMatch = decimal.TryParse(term, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedAmount)
            || decimal.TryParse(term, NumberStyles.Number, CultureInfo.CurrentCulture, out parsedAmount);

        return row.ReferenceNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(row.PartyName) && row.PartyName.Contains(term, StringComparison.OrdinalIgnoreCase))
            || (numericMatch && (
                row.RevenueAfterTax == parsedAmount
                || row.TotalCost == parsedAmount
                || row.ProfitBeforeTax == parsedAmount
                || row.ProfitAfterTax == parsedAmount));
    }

    private static string NormalizeType(string? type) =>
        type is null
            ? "all"
            : type.Trim() switch
            {
                var value when string.Equals(value, "all", StringComparison.OrdinalIgnoreCase) => "all",
                var value when string.Equals(value, "sale", StringComparison.OrdinalIgnoreCase) => "sale",
                var value when string.Equals(value, "saleReturn", StringComparison.OrdinalIgnoreCase) => "saleReturn",
                var value when string.Equals(value, "inventoryAdjustment", StringComparison.OrdinalIgnoreCase) => "inventoryAdjustment",
                _ => "all",
            };

    private static decimal? CalculateMarginPercent(decimal profitAfterTax, decimal totalCost) =>
        totalCost == 0m
            ? null
            : Math.Round(profitAfterTax * 100m / totalCost, 2, MidpointRounding.AwayFromZero);
}

public sealed record ProfitLossReportBuildResult(
    IReadOnlyList<ProfitLossReportItemDto> Items,
    ProfitLossSummaryDto Summary,
    DateOnly From,
    DateOnly To,
    string Type,
    string? Search);
