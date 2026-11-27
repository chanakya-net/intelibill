namespace Intelibill.Application.Features.Exports.ProfitLoss.DTOs;

public sealed record ProfitLossExportMetadataDto(
    string ShopName,
    string? ShopAddress,
    string GeneratedBy,
    DateTimeOffset GeneratedAt,
    DateOnly From,
    DateOnly To,
    string Type,
    string? Search,
    string Format,
    string BusinessTimeZoneId = "Asia/Kolkata",
    int EconomicsVersion = 2,
    string CostBasis = "recorded-sale-cost/1");
