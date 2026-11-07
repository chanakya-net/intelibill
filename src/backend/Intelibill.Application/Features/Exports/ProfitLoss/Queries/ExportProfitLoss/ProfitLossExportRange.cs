namespace Intelibill.Application.Features.Exports.ProfitLoss.Queries.ExportProfitLoss;

internal static class ProfitLossExportRange
{
    public const string ErrorCode = "Exports.ProfitLoss.InvalidDateRange";
    public const string Message = "'from' date must be before or equal to 'to' date.";

    public static bool IsValid(DateOnly from, DateOnly to) => from <= to;
}
