namespace Intelibill.Application.Features.Exports.ProfitLoss.DTOs;

public sealed record ProfitLossExportSummaryDto(
    decimal NetProfitAfterTax,
    decimal RevenueIncludingTax,
    decimal TotalCost,
    decimal? AverageMarginPercent,
    int InvoiceCount,
    int ReturnCount,
    int AdjustmentCount);
