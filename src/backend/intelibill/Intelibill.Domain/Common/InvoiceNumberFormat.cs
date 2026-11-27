using System.Globalization;
using System.Text.RegularExpressions;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Common;

/// <summary>
/// Fiscal invoice numbers: <c>{series}/{yyyy}/{counter}</c>, e.g. <c>A/2627/000001</c>. Rule 46 allows
/// at most 16 characters, unique within the fiscal year for the registration.
/// </summary>
public static partial class InvoiceNumberFormat
{
    public const int MaxLength = 16;
    public const int CounterPadding = 6;
    public const int MaxSeriesCodeLength = 4;
    public const string DefaultSeriesCode = "A";

    [GeneratedRegex("^[A-Z0-9]{1,4}$", RegexOptions.CultureInvariant, 250)]
    private static partial Regex SeriesShape();

    public static string? NormalizeSeriesCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    public static bool IsValidSeriesCode(string? value) =>
        NormalizeSeriesCode(value) is { } code && SeriesShape().IsMatch(code);

    public static string Prefix(string seriesCode, FiscalYear fiscalYear) => $"{seriesCode}/{fiscalYear.ShortLabel}/";

    public static string Format(string prefix, int number, int padding = CounterPadding) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}{number.ToString($"D{padding}", CultureInfo.InvariantCulture)}");

    /// <summary>The largest counter whose formatted number still fits <see cref="MaxLength"/>.</summary>
    public static int Capacity(string prefix)
    {
        var digits = MaxLength - prefix.Length;
        if (digits <= 0)
            return 0;

        return digits >= 10 ? int.MaxValue : (int)Math.Min(int.MaxValue, Math.Pow(10, digits) - 1);
    }
}
