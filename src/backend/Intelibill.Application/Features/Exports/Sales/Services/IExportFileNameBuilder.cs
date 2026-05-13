namespace Intelibill.Application.Features.Exports.Sales.Services;

public interface IExportFileNameBuilder
{
    /// <summary>
    /// Builds a sanitized export filename using shop name, format, level/tally indicator, and date range.
    /// </summary>
    /// <param name="shopName">The shop name to sanitize and include in filename.</param>
    /// <param name="format">The export format (xlsx, pdf, tallyXml).</param>
    /// <param name="level">The export level (summary, lineItems) or null for tally exports.</param>
    /// <param name="startDate">Export period start date.</param>
    /// <param name="endDate">Export period end date.</param>
    /// <returns>A sanitized filename with extension (e.g., "my-shop-sales-summary-2025-01-01-to-2025-01-31.xlsx").</returns>
    string BuildFileName(
        string shopName,
        string format,
        string? level,
        DateOnly startDate,
        DateOnly endDate);

    /// <summary>
    /// Maps an export format to its corresponding MIME content type.
    /// </summary>
    /// <param name="format">The export format (xlsx, pdf, tallyXml).</param>
    /// <returns>The MIME type string for the format.</returns>
    string GetContentType(string format);
}
