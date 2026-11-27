namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record ProfitLossRowEconomicsDto(
    int Version, decimal NetSalesExcludingTax, decimal Cogs, decimal StockLoss,
    decimal ReturnedGoodsWastageIncludedInCogs, decimal GrossTradingProfit,
    decimal? GrossMarginPercent, decimal? MarkupPercent, bool HasUnmodeledServiceCost);

public sealed record ProfitLossEconomicsDto(
    int Version, string CostBasis, decimal NetSalesExcludingTax, decimal Cogs, decimal StockLoss,
    decimal ReturnedGoodsWastageIncludedInCogs, decimal GrossTradingProfit,
    decimal? OperatingExpenses, decimal? OperatingProfit, decimal? GrossMarginPercent, decimal? MarkupPercent,
    int UnclassifiedExpenseCount, decimal UnclassifiedExpenseAmount, string OperatingProfitStatus,
    IReadOnlyList<string> Limitations);
