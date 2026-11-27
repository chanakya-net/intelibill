namespace Intelibill.Application.Features.Exports.Sales;

public static class SalesExportLevel
{
    public const string Summary = "summary";
    public const string LineItems = "lineItems";

    public static readonly string[] SupportedLevels = { Summary, LineItems };

    public static bool IsSupported(string level) =>
        SupportedLevels.Contains(level, StringComparer.OrdinalIgnoreCase);
}
