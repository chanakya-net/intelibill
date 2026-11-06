namespace Intelibill.Application.Features.Exports.Sales.Queries.ExportSales;

internal static class SalesExportRange
{
    private const int MaxDays = 366;

    /// <summary>The reason the range is refused, or null when it is allowed.</summary>
    public static string? Validate(DateOnly start, DateOnly end)
    {
        if (start > end)
            return "Start date must be on or before end date.";

        return end.DayNumber - start.DayNumber > MaxDays
            ? "Date range must not exceed 366 days."
            : null;
    }
}
