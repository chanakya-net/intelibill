namespace Intelibill.Application.Features.Sales.DTOs;

public static class SaleHistoryStatus
{
    public const string Paid = "paid";
    public const string PartiallyPaid = "partiallyPaid";
    public const string Refunded = "refunded";
    public const string Unknown = "unknown";

    private static readonly HashSet<string> ValidAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "all",
        Paid,
        PartiallyPaid,
        Refunded,
        Unknown,
        "returned", // backward-compat alias for refunded
    };

    public static bool IsValid(string? status) =>
        string.IsNullOrWhiteSpace(status) || ValidAliases.Contains(status.Trim());
}
