namespace Intelibill.Integration.Tests;

/// <summary>
/// A declared Regular GST profile for shops created through the API: sales and offline ranges are
/// refused until a shop has one. Every shop gets its own GSTIN, since a series belongs to one
/// shop per registration.
/// </summary>
internal static class TestTaxProfile
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static object Regular { get; } = new { stateCode = "29", taxRegime = "Regular", eInvoiceStatus = "BelowThreshold" };

    /// <summary>A checksum-valid Karnataka GSTIN with a random PAN.</summary>
    public static string NewGstin(string stateCode = "29")
    {
        var letters = new char[6];
        for (var i = 0; i < letters.Length; i++)
            letters[i] = (char)('A' + Random.Shared.Next(26));
        var digits = Random.Shared.Next(10_000).ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
        var body = $"{stateCode}{new string(letters, 0, 5)}{digits}{letters[5]}1Z";

        var sum = 0;
        for (var i = 0; i < body.Length; i++)
        {
            var product = Alphabet.IndexOf(body[i], StringComparison.Ordinal) * (i % 2 == 0 ? 1 : 2);
            sum += (product / Alphabet.Length) + (product % Alphabet.Length);
        }

        return body + Alphabet[(Alphabet.Length - (sum % Alphabet.Length)) % Alphabet.Length];
    }
}
