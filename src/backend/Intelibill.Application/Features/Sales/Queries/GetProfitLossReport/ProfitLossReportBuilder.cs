using System.Globalization;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Exports.Accounting;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Services.Pricing;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Queries.GetProfitLossReport;

public sealed class ProfitLossReportBuilder(
    ISaleRepository saleRepository,
    ReturnAdjustmentReader returnAdjustmentReader,
    IInventoryAdjustmentRepository inventoryAdjustmentRepository,
    IBusinessClock businessClock,
    IExpenseRepository expenseRepository,
    SaleCancellationAdjustmentReader cancellationAdjustmentReader)
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
        var returnCosts = await returnAdjustmentReader.ReadRecordedCostAllocationsAsync(shopId, returnAdjustments, salesInRange, cancellationToken);
        var cancellations = await cancellationAdjustmentReader.ReadAsync(shopId, from, to, cancellationToken);
        var window = businessClock.GetUtcWindow(from, to, timeZoneId);
        var expenses = await expenseRepository.GetByShopAndDateRangeAsync(shopId, from, to, cancellationToken);
        var adjustmentsInRange = await inventoryAdjustmentRepository.GetPostedInRangeAsync(
            shopId,
            window,
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
                sale.InvoiceDocument?.Buyer.Name ?? sale.CustomerName,
                saleTotalCost,
                WastageCost: 0m,
                revenueExclTax,
                revenueInclTax,
                revenueInclTax - saleTotalCost,
                revenueExclTax - saleTotalCost,
                CalculateMarginPercent(revenueExclTax - saleTotalCost, saleTotalCost),
                ProfitLossReportRowTypes.Sale,
                InventoryAdjustmentId: null,
                Economics: RowEconomics(revenueExclTax,
                    sale.Items.Where(i => i.LineType != SaleLineType.Service).Sum(i => SaleEconomicsCalculator.RecordedCost(i.Quantity, i.CostPrice)),
                    0m, 0m, sale.Items.Any(i => i.LineType == SaleLineType.Service))));
        }

        foreach (var entry in cancellations)
            report.Add(new ProfitLossReportItemDto(entry.SaleId, $"{entry.InvoiceNumber} / {entry.ReferenceNumber}",
                entry.PostedAt, entry.CustomerName, -entry.RawCostAmount, 0m, -entry.RevenueAmount, -entry.TotalAmount,
                -entry.TotalAmount + entry.RawCostAmount, -entry.RevenueAmount + entry.RawCostAmount,
                CalculateMarginPercent(-entry.RevenueAmount + entry.RawCostAmount, -entry.RawCostAmount),
                ProfitLossReportRowTypes.SaleCancellation, null, Economics: RowEconomics(-entry.RevenueAmount, -entry.RecordedCogs, 0m, 0m, false)));

        foreach (var entry in returnAdjustments)
            report.Add(BuildReturnRow(entry, returnCosts));

        foreach (var adjustment in adjustmentsInRange.Where(a => a.Direction == InventoryAdjustmentDirection.Decrease))
        {
            if (window.Contains(adjustment.PerformedAt)) AddAdjustment(adjustment.PerformedAt, adjustment.CostImpact, false);
            if (adjustment.IsVoided && adjustment.VoidedAt is { } voidedAt && window.Contains(voidedAt))
                AddAdjustment(voidedAt, -adjustment.CostImpact, true);

            void AddAdjustment(DateTimeOffset at, decimal loss, bool reversal) => report.Add(new ProfitLossReportItemDto(
                SaleId: null, adjustment.AdjustmentNumber, at, PartyName: null,
                TotalCost: 0m, WastageCost: loss, RevenueBeforeTax: 0m, RevenueAfterTax: 0m,
                ProfitBeforeTax: -loss, ProfitAfterTax: -loss, MarginPercent: null,
                RowType: ProfitLossReportRowTypes.InventoryAdjustment, InventoryAdjustmentId: adjustment.Id,
                IsReversal: reversal, Economics: RowEconomics(0m, 0m, loss, 0m, false)));
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
            AdjustmentCount: filtered.Count(row => row.RowType == ProfitLossReportRowTypes.InventoryAdjustment),
            Economics: SummaryEconomics(filtered, expenses, normalizedType != "all" || !string.IsNullOrWhiteSpace(search)),
            CancellationAmount: -filtered.Where(r => r.RowType == ProfitLossReportRowTypes.SaleCancellation).Sum(r => r.RevenueAfterTax),
            CancellationCount: filtered.Count(r => r.RowType == ProfitLossReportRowTypes.SaleCancellation));

        return new ProfitLossReportBuildResult(filtered, summary, from, to, normalizedType, search);
    }

    /// <summary>
    /// A return's effect as posted: revenue reduced by the refund, less its output tax as the snapshot
    /// classified it, and the cost of restocked goods back in stock. A void is the same row negated,
    /// in the period it was voided. An unreviewed legacy return has no tax split, so its whole refund
    /// reduces revenue before tax and the row says it needs review.
    /// </summary>
    private static ProfitLossReportItemDto BuildReturnRow(ReturnAdjustmentEntry entry, IReadOnlyDictionary<(Guid ReturnId, Guid SaleItemId), decimal> costs)
    {
        decimal AllocatedCost(ReturnAdjustmentLine line) => costs.TryGetValue((entry.SaleReturnId, line.SaleItemId), out var allocated)
            ? (entry.IsReversal ? -allocated : allocated)
            : SaleEconomicsCalculator.RecordedCost(line.Quantity, line.OriginalCostPrice);
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
            NeedsAccountingReview: entry.Status == ReturnAccountingStatus.NeedsReview,
            Economics: RowEconomics(returnRevenueExclTax,
                -goodsLines.Where(l => l.Condition == SaleReturnCondition.Restockable).Sum(AllocatedCost),
                0m, goodsLines.Where(l => l.Condition == SaleReturnCondition.Wastage).Sum(AllocatedCost),
                entry.Lines.Any(l => l.LineType == SaleLineType.Service)));
    }

    private static ProfitLossRowEconomicsDto RowEconomics(decimal revenue, decimal cost, decimal loss, decimal wastage, bool service)
    {
        var line = SaleEconomicsCalculator.Calculate(revenue, cost + loss);
        var markup = cost == 0m ? (decimal?)null : Math.Round(line.GrossProfit / cost * 100m, 2, MidpointRounding.AwayFromZero);
        return new(2, line.NetRevenue, cost, loss, wastage, line.GrossProfit, line.GrossMarginPercent, markup, service);
    }

    private static ProfitLossEconomicsDto SummaryEconomics(IReadOnlyList<ProfitLossReportItemDto> rows, IReadOnlyList<Domain.Entities.Expense> expenses, bool filtered)
    {
        var revenue = rows.Sum(r => r.Economics!.NetSalesExcludingTax);
        var cost = rows.Sum(r => r.Economics!.Cogs);
        var loss = rows.Sum(r => r.Economics!.StockLoss);
        var trading = RowEconomics(revenue, cost, loss, rows.Sum(r => r.Economics!.ReturnedGoodsWastageIncludedInCogs), false);
        var active = expenses.Where(e => !e.IsVoided).ToList();
        var unclassified = active.Where(e => e.Treatment == ExpenseTreatment.Unclassified).ToList();
        var review = rows.Any(r => r.NeedsAccountingReview);
        var service = rows.Any(r => r.Economics!.HasUnmodeledServiceCost);
        var limitations = new List<string> { "purchase-tax-recoverability-unmodeled", "historical-cost-snapshots-preserved", "receipt-cost-corrections-not-revalued" };
        if (filtered) limitations.Add("filtered-activity");
        if (unclassified.Count > 0) limitations.Add("unclassified-expenses");
        if (review) limitations.Add("return-accounting-review");
        if (service) limitations.Add("service-costs-unmodeled");
        var status = filtered ? "FilteredActivity" : unclassified.Count > 0 ? "UnclassifiedExpenses" : review ? "AccountingReview" : service ? "ServiceCostsUnmodeled" : "Complete";
        decimal? operatingExpenses = filtered ? null : active.Where(e => e.Treatment == ExpenseTreatment.Operating).Sum(e => e.Amount);
        return new(2, "recorded-sale-cost/1", revenue, cost, loss, trading.ReturnedGoodsWastageIncludedInCogs,
            trading.GrossTradingProfit, operatingExpenses, status == "Complete" ? trading.GrossTradingProfit - operatingExpenses : null,
            trading.GrossMarginPercent, trading.MarkupPercent, unclassified.Count, unclassified.Sum(e => e.Amount), status, limitations);
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
                row.Economics?.NetSalesExcludingTax == parsedAmount
                || row.Economics?.Cogs == parsedAmount
                || row.Economics?.StockLoss == parsedAmount
                || row.Economics?.GrossTradingProfit == parsedAmount
                || row.Economics?.ReturnedGoodsWastageIncludedInCogs == parsedAmount
                || row.RevenueAfterTax == parsedAmount
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
                var value when string.Equals(value, "saleCancellation", StringComparison.OrdinalIgnoreCase) => "saleCancellation",
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
