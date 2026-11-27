using System.Text.RegularExpressions;

namespace Intelibill.Domain.Common;

/// <summary>
/// GSTIN shape and check-character validation. A valid GSTIN says which state a registration is
/// in; it never says which regime the registration is under.
/// </summary>
public static partial class Gstin
{
    public const int Length = 15;
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    [GeneratedRegex("^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$", RegexOptions.CultureInvariant, 250)]
    private static partial Regex Shape();

    /// <summary>Trimmed and upper-cased, or null when blank.</summary>
    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    /// <summary>True when the value has the GSTIN shape, a known state code and a correct check character.</summary>
    public static bool IsValid(string? value)
    {
        var gstin = Normalize(value);
        if (gstin is null || gstin.Length != Length || !Shape().IsMatch(gstin))
            return false;

        return GstStateCodes.IsKnown(gstin[..2]) && gstin[14] == CheckCharacter(gstin);
    }

    /// <summary>The state code a GSTIN was issued in (its first two digits).</summary>
    public static string StateCodeOf(string gstin) => Normalize(gstin)![..2];

    private static char CheckCharacter(string gstin)
    {
        var sum = 0;
        for (var i = 0; i < Length - 1; i++)
        {
            var product = Alphabet.IndexOf(gstin[i], StringComparison.Ordinal) * (i % 2 == 0 ? 1 : 2);
            sum += (product / Alphabet.Length) + (product % Alphabet.Length);
        }

        return Alphabet[(Alphabet.Length - (sum % Alphabet.Length)) % Alphabet.Length];
    }
}
