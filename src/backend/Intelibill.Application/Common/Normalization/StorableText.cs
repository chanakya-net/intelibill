namespace Intelibill.Application.Common.Normalization;

/// <summary>
/// Whether client text can be stored in PostgreSQL as given. <c>text</c> and <c>jsonb</c> cannot hold
/// U+0000, and an unpaired UTF-16 surrogate has no UTF-8 encoding (the strict encoder refuses it,
/// and <c>jsonb</c> refuses its <c>\u</c> escape), so either would fail the save with a server error.
/// </summary>
public static class StorableText
{
    /// <summary>True for null, or text with no U+0000 and every surrogate paired.</summary>
    public static bool IsStorable(string? value)
    {
        if (value is null)
            return true;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\0')
                return false;

            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                    continue;
                }

                return false;
            }

            if (char.IsLowSurrogate(c))
                return false;
        }

        return true;
    }
}
