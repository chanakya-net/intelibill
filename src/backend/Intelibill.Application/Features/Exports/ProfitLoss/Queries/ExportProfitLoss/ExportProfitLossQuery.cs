namespace Intelibill.Application.Features.Exports.ProfitLoss.Queries.ExportProfitLoss;

public sealed record ExportProfitLossQuery(
    Guid UserId,
    Guid ShopId,
    DateOnly From,
    DateOnly To,
    string? Type = null,
    string? Search = null,
    string? Format = "xlsx");
