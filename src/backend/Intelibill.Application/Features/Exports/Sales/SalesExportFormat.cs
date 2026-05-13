namespace Intelibill.Application.Features.Exports.Sales;

public static class SalesExportFormat
{
    public const string Xlsx = "xlsx";
    public const string Pdf = "pdf";
    public const string TallyXml = "tallyXml";
    public const string Summary = "summary";
    public const string LineItems = "lineItems";

    public static readonly string[] SupportedFormats = { Xlsx, Pdf, TallyXml, Summary, LineItems };

    public static bool IsSupported(string format) =>
        SupportedFormats.Contains(format, StringComparer.OrdinalIgnoreCase);
}
