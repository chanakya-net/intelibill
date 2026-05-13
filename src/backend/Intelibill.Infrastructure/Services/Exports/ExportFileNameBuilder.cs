using System.Text.RegularExpressions;
using Intelibill.Application.Features.Exports.Sales.Services;

namespace Intelibill.Infrastructure.Services.Exports;

public sealed class ExportFileNameBuilder : IExportFileNameBuilder
{
    public string BuildFileName(
        string shopName,
        string format,
        string? level,
        DateOnly startDate,
        DateOnly endDate)
    {
        var sanitizedShop = SanitizeShopName(shopName);
        var extension = GetExtension(format);
        var level_or_tally = GetLevelOrTally(format, level);

        return $"{sanitizedShop}-sales-{level_or_tally}-{startDate:yyyy-MM-dd}-to-{endDate:yyyy-MM-dd}{extension}";
    }

    public string GetContentType(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "pdf" => "application/pdf",
            "tallyxml" => "application/xml",
            _ => "application/octet-stream"
        };
    }

    private static string SanitizeShopName(string shopName)
    {
        // Lowercase
        var lower = shopName.ToLowerInvariant();

        // Replace whitespace with hyphens
        var with_hyphens = Regex.Replace(lower, @"\s+", "-");

        // Remove unsafe characters (keep only alphanumeric and hyphens)
        var safe = Regex.Replace(with_hyphens, @"[^a-z0-9\-]", string.Empty);

        // Collapse repeated hyphens
        var collapsed = Regex.Replace(safe, "-{2,}", "-");

        // Trim hyphens from start/end
        var trimmed = collapsed.Trim('-');

        // Fallback to "shop" if empty
        return string.IsNullOrEmpty(trimmed) ? "shop" : trimmed;
    }

    private static string GetExtension(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "xlsx" => ".xlsx",
            "pdf" => ".pdf",
            "tallyxml" => ".xml",
            _ => ".bin"
        };
    }

    private static string GetLevelOrTally(string format, string? level)
    {
        if (string.Equals(format, "tallyXml", StringComparison.OrdinalIgnoreCase))
        {
            return "tally";
        }

        return level?.ToLowerInvariant() ?? "summary";
    }
}
