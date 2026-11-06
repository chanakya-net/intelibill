using System.Globalization;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Exports.Accounting;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Queries.GetProfitLossReport;

public sealed class ProfitLossReportBuilder(
    ISaleRepository saleRepository,
    ReturnAdjustmentReader returnAdjustmentReader,
    IInventoryAdjustmentRepository inventoryAdjustmentRepository,
    IBusinessClock businessClock)
{
    public async Task<ProfitLossReportBuildResult> BuildAsync(
        Guid shopId,
        string timeZoneId,
        DateOnly from,
        DateOnly to,
        string? type,
        string? search,
        CancellationToken cancellationToken)
    {
        var normalizedType = NormalizeType(type);

        var salesInRange = await saleRepository.GetByShopAndDateRangeAsync(shopId, from, to, cancellationToken);
        var returnAdjustments = await returnAdjustmentReader.ReadAsync(shopId, from, to, salesInRange, cancellationToken);
        var adjustmentsInRange = await inventoryAdjustmentRepository.GetByShopAndDateRangeAsync(
            shopId,
            businessClock.GetUtcWindow(from, to, timeZoneId),
            cancellationToken);

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

        foreach (var entry in returnAdjustments)
            report.Add(BuildReturnRow(entry));

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
            ReturnCount: filtered.Count(row => row.RowType == ProfitLossReportRowTypes.SaleReturn && !row.IsReversal),
            AdjustmentCount: filtered.Count(row => row.RowType == ProfitLossReportRowTypes.InventoryAdjustment));

        return new ProfitLossReportBuildResult(filtered, summary, from, to, normalizedType, search);
    }

    /// <summary>
    /// A return's effect as posted: revenue reduced by the refund, less its output tax as the snapshot
    /// classified it, and the cost of restocked goods back in stock. A void is the same row negated,
    /// in the period it was voided. An unreviewed legacy return has no tax split, so its whole refund
    /// reduces revenue before tax and the row says it needs review.
    /// </summary>
    private static ProfitLossReportItemDto BuildReturnRow(ReturnAdjustmentEntry entry)
    {
        var goodsLines = entry.Lines.Where(l => l.LineType != SaleLineType.Service).ToList();
        var restockableCost = goodsLines
            .Where(l => l.Condition == SaleReturnCondition.Restockable)
            .Sum(l => l.OriginalCostPrice * l.Quantity);
        var wastageCost = goodsLines
            .Where(l => l.Condition == SaleReturnCondition.Wastage)
            .Sum(l => l.OriginalCostPrice * l.Quantity);
        var returnCostImpact = -restockableCost;
        var returnRevenueInclTax = -entry.FinancialAdjustment;
        var returnRevenueExclTax = -(entry.FinancialAdjustment - (entry.TaxAdjustment ?? 0m));

        return new ProfitLossReportItemDto(
            entry.SaleId,
            $"{entry.InvoiceNumber} / {entry.Label}",
            entry.PostedAt,
            entry.CustomerName,
            returnCostImpact,
            wastageCost,
            returnRevenueExclTax,
            returnRevenueInclTax,
            returnRevenueInclTax - returnCostImpact,
            returnRevenueExclTax - returnCostImpact,
            CalculateMarginPercent(returnRevenueExclTax - returnCostImpact, returnCostImpact),
            ProfitLossReportRowTypes.SaleReturn,
            InventoryAdjustmentId: null,
            IsReversal: entry.IsReversal,
            NeedsAccountingReview: entry.Status == ReturnAccountingStatus.NeedsReview);
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
