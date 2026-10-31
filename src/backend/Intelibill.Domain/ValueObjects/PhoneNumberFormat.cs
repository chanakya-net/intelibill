namespace Intelibill.Domain.ValueObjects;

/// <summary>
/// Normalises a typed phone number to E.164, and refuses anything it would have to guess at.
/// <para>
/// A verification code is money and an identity, so sending one to the wrong subscriber is not a
/// recoverable mistake. The rule is therefore: an international number is accepted as given, a
/// bare national number is accepted only where this file states that country's rule, and
/// everything else is rejected with the input untouched. A leading zero is never stripped and
/// never prefixed — "0987654321" is a trunk-prefixed number, not a subscriber number, and reading
/// it as "+910987654321" or "+919876543210" are both inventions.
/// </para>
/// </summary>
public static class PhoneNumberFormat
{
    private const int MinDigits = 8;
    private const int MaxDigits = 15;

    /// <summary>
    /// Produces the E.164 form of <paramref name="input"/>, or false when the input is not
    /// unambiguously a phone number. <paramref name="defaultCountryCode"/> names the country a
    /// bare national number may be read against; a country only counts once its national rule is
    /// written here, so an unknown default never makes a bare number acceptable.
    /// </summary>
    public static bool TryNormalize(string? input, string? defaultCountryCode, out string e164)
    {
        e164 = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var compact = Compact(input);
        if (compact.Length == 0)
            return false;

        if (compact[0] == '+')
        {
            var digits = compact[1..];
            if (!IsDigits(digits) || digits.Length < MinDigits || digits.Length > MaxDigits || digits[0] == '0')
                return false;

            e164 = "+" + digits;
            return true;
        }

        if (!IsDigits(compact))
            return false;

        return TryNational(compact, defaultCountryCode?.Trim(), out e164);
    }

    /// <summary>
    /// Hides all but the last three digits, keeping the country code so somebody can tell which of
    /// their numbers a code went to without the full number appearing in a response or a log.
    /// </summary>
    public static string Mask(string e164)
    {
        if (string.IsNullOrEmpty(e164))
            return string.Empty;

        if (e164.Length <= 6)
            return new string('•', e164.Length);

        return string.Concat(e164.AsSpan(0, 3), new string('•', e164.Length - 6), e164.AsSpan(e164.Length - 3));
    }

    /// <summary>
    /// National rules, one country at a time and on purpose. Adding a country is a deliberate edit
    /// here, not a side effect of changing a configuration value.
    /// </summary>
    private static bool TryNational(string digits, string? defaultCountryCode, out string e164)
    {
        e164 = string.Empty;

        switch (defaultCountryCode)
        {
            // India: mobile numbers are ten digits beginning 6, 7, 8 or 9. Anything else ten digits
            // long is a landline, a short code or a mistake, and none of those receives an SMS.
            case "+91" when digits.Length == 10 && digits[0] is >= '6' and <= '9':
                e164 = "+91" + digits;
                return true;

            default:
                return false;
        }
    }

    private static string Compact(string input)
    {
        var buffer = new char[input.Length];
        var length = 0;

        foreach (var character in input)
        {
            if (char.IsWhiteSpace(character) || character is '-' or '(' or ')' or '.' or ' ')
                continue;

            buffer[length++] = character;
        }

        return new string(buffer, 0, length);
    }

    private static bool IsDigits(string value)
    {
        if (value.Length == 0)
            return false;

        foreach (var character in value)
        {
            // Only ASCII digits: a Devanagari or Arabic-Indic digit parses as a number but is not
            // what a carrier will dial.
            if (character is < '0' or > '9')
                return false;
        }

        return true;
    }
}
