namespace Intelibill.Domain.Common;

/// <summary>
/// The GST state and union-territory codes a domestic supply can be made in. Codes retired by
/// mergers (25, 28) and the non-domestic codes (96 other country, 97 other territory) are
/// deliberately absent: a document naming them is not one this product issues.
/// </summary>
public static class GstStateCodes
{
    public sealed record GstState(string Code, string Name, bool IsUnionTerritoryWithoutLegislature);

    private static readonly GstState[] States =
    [
        new("01", "Jammu and Kashmir", false),
        new("02", "Himachal Pradesh", false),
        new("03", "Punjab", false),
        new("04", "Chandigarh", true),
        new("05", "Uttarakhand", false),
        new("06", "Haryana", false),
        new("07", "Delhi", false),
        new("08", "Rajasthan", false),
        new("09", "Uttar Pradesh", false),
        new("10", "Bihar", false),
        new("11", "Sikkim", false),
        new("12", "Arunachal Pradesh", false),
        new("13", "Nagaland", false),
        new("14", "Manipur", false),
        new("15", "Mizoram", false),
        new("16", "Tripura", false),
        new("17", "Meghalaya", false),
        new("18", "Assam", false),
        new("19", "West Bengal", false),
        new("20", "Jharkhand", false),
        new("21", "Odisha", false),
        new("22", "Chhattisgarh", false),
        new("23", "Madhya Pradesh", false),
        new("24", "Gujarat", false),
        new("26", "Dadra and Nagar Haveli and Daman and Diu", true),
        new("27", "Maharashtra", false),
        new("29", "Karnataka", false),
        new("30", "Goa", false),
        new("31", "Lakshadweep", true),
        new("32", "Kerala", false),
        new("33", "Tamil Nadu", false),
        new("34", "Puducherry", false),
        new("35", "Andaman and Nicobar Islands", true),
        new("36", "Telangana", false),
        new("37", "Andhra Pradesh", false),
        new("38", "Ladakh", true),
    ];

    private static readonly Dictionary<string, GstState> ByCode = States.ToDictionary(s => s.Code, StringComparer.Ordinal);

    public static IReadOnlyList<GstState> All => States;

    public static GstState? Find(string? code) =>
        code is not null && ByCode.TryGetValue(code.Trim(), out var state) ? state : null;

    public static bool IsKnown(string? code) => Find(code) is not null;
}
