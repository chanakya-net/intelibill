namespace Intelibill.Application.Features.Exports.ProfitLoss.DTOs;

public sealed record ProfitLossExportRowDto(
    string ReferenceNumber,
    DateTimeOffset OccurredAt,
    string RowType,
    string? CustomerName,
    decimal TotalCost,
    decimal WastageCost,
    decimal RevenueBeforeTax,
    decimal RevenueAfterTax,
    decimal ProfitBeforeTax,
    decimal ProfitAfterTax,
    decimal? MarginPercent);
