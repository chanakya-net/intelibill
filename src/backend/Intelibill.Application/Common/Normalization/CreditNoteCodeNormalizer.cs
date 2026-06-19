namespace Intelibill.Application.Common.Normalization;

public static class CreditNoteCodeNormalizer
{
    public static string Normalize(string code)
    {
        var trimmed = code.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        var compact = new string(trimmed
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

        return compact;
    }
}
