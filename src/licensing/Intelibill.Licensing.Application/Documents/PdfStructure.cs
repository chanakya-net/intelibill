using System.Globalization;

namespace Intelibill.Licensing.Application.Documents;

// A structural envelope check only. The application never interprets or renders document objects.
internal static class PdfStructure
{
    public static bool HasEnvelope(ReadOnlySpan<byte> content)
    {
        if (!HasHeader(content)) return false;
        content = TrimEnd(content);
        if (!content.EndsWith("%%EOF"u8) || !IsLineBreak(content[^6])) return false;
        var tailStart = Math.Max(9, content.Length - 1024);
        var marker = content[tailStart..^5].LastIndexOf("startxref"u8);
        if (marker < 0) return false;
        marker += tailStart;
        if (!IsLineBreak(content[marker - 1]) || !IsWhite(content[marker + 9])) return false;

        var pointer = TrimEnd(TrimStart(content[(marker + 9)..^5]));
        if (!TryNumber(pointer, out var offset) || offset < 9 || offset >= marker || !IsWhite(content[offset - 1])) return false;
        var section = TrimEnd(content[offset..marker]);
        return section.StartsWith("xref"u8) ? HasTable(section, content[..offset]) : HasStream(section);
    }

    private static bool HasHeader(ReadOnlySpan<byte> content) =>
        content.Length >= 32 && content.StartsWith("%PDF-"u8) && content[6] == '.' && IsLineBreak(content[8])
        && (content[5] == '1' && content[7] is >= (byte)'0' and <= (byte)'7' || content[5] == '2' && content[7] == '0');

    private static bool HasTable(ReadOnlySpan<byte> section, ReadOnlySpan<byte> objects)
    {
        if (section.Length < 26 || !IsWhite(section[4])) return false;
        var trailer = section.LastIndexOf("trailer"u8);
        if (trailer < 25 || !IsWhite(section[trailer - 1])) return false;
        var entries = TrimEnd(section[4..trailer]);
        var hasIndirectObject = false;
        while (!entries.IsEmpty)
        {
            if (!TryNumber(NextToken(ref entries), out var first) || !TryNumber(NextToken(ref entries), out var count)
                || count < 1 || count > entries.Length / 17 || first > int.MaxValue - count) return false;
            for (var index = 0; index < count; index++)
            {
                if (!TryTableEntry(ref entries, objects.Length, out var offset, out var generation)) return false;
                if (!hasIndirectObject && offset is { } target)
                    hasIndirectObject = MatchesObject(objects[target..], first + index, generation);
            }
            entries = TrimStart(entries);
        }

        var dictionary = TrimEnd(TrimStart(section[(trailer + 7)..]));
        return dictionary.StartsWith("<<"u8) && dictionary.EndsWith(">>"u8) && HasName(dictionary, "/Size"u8)
            && (HasName(dictionary, "/Prev"u8) || hasIndirectObject && HasName(dictionary, "/Root"u8));
    }

    private static bool TryTableEntry(ref ReadOnlySpan<byte> entries, int objectLimit, out int? offset, out int generation)
    {
        offset = null;
        generation = 0;
        var position = NextToken(ref entries);
        var generationToken = NextToken(ref entries);
        var flag = NextToken(ref entries);
        if (position.Length != 10 || generationToken.Length != 5 || !TryNumber(generationToken, out generation)
            || generation > 65535 || !TryNumber(position, out var number)) return false;
        if (flag is [(byte)'f']) return true;
        if (flag is not [(byte)'n'] || number < 9 || number >= objectLimit) return false;
        offset = number;
        return true;
    }

    private static bool MatchesObject(ReadOnlySpan<byte> content, int number, int generation)
    {
        content = content[..Math.Min(content.Length, 64)];
        return TryNumber(NextToken(ref content), out var id) && id == number
            && TryNumber(NextToken(ref content), out var revision) && revision == generation
            && StartsWithObjectKeyword(TrimStart(content));
    }

    private static bool HasStream(ReadOnlySpan<byte> section)
    {
        if (!TryNumber(NextToken(ref section), out var number) || number < 1 || !TryNumber(NextToken(ref section), out _))
            return false;
        section = TrimStart(section);
        if (!StartsWithObjectKeyword(section)) return false;
        section = TrimStart(section[3..]);
        var opening = section.IndexOf("stream"u8);
        if (opening < 4 || opening + 6 >= section.Length || !IsLineBreak(section[opening + 6])
            || !section.StartsWith("<<"u8) || !TrimEnd(section[..opening]).EndsWith(">>"u8)
            || !section.EndsWith("endobj"u8) || !TrimEnd(section[..^6]).EndsWith("endstream"u8)) return false;
        var dictionary = section[..opening];
        return HasName(dictionary, "/Type"u8, "/XRef"u8) && HasName(dictionary, "/Size"u8)
            && HasName(dictionary, "/W"u8) && HasName(dictionary, "/Length"u8)
            && (HasName(dictionary, "/Root"u8) || HasName(dictionary, "/Prev"u8));
    }

    private static bool StartsWithObjectKeyword(ReadOnlySpan<byte> content) =>
        content.Length > 3 && content.StartsWith("obj"u8) && IsDelimiter(content[3]);

    private static bool HasName(ReadOnlySpan<byte> dictionary, ReadOnlySpan<byte> name, ReadOnlySpan<byte> value = default)
    {
        while (dictionary.IndexOf(name) is >= 0 and var index)
        {
            var preceded = index > 0 && IsDelimiter(dictionary[index - 1]);
            dictionary = dictionary[(index + name.Length)..];
            if (!preceded || dictionary.IsEmpty || !IsDelimiter(dictionary[0])) continue;
            if (value.IsEmpty) return true;
            var token = TrimStart(dictionary);
            if (token.Length > value.Length && token.StartsWith(value) && IsDelimiter(token[value.Length])) return true;
        }
        return false;
    }

    private static ReadOnlySpan<byte> NextToken(ref ReadOnlySpan<byte> bytes)
    {
        bytes = TrimStart(bytes);
        var length = 0;
        while (length < bytes.Length && !IsWhite(bytes[length])) length++;
        var token = bytes[..length];
        bytes = bytes[length..];
        return token;
    }

    private static bool TryNumber(ReadOnlySpan<byte> token, out int number) =>
        int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out number);

    private static ReadOnlySpan<byte> TrimStart(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty && IsWhite(bytes[0])) bytes = bytes[1..];
        return bytes;
    }

    private static ReadOnlySpan<byte> TrimEnd(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty && IsWhite(bytes[^1])) bytes = bytes[..^1];
        return bytes;
    }

    private static bool IsLineBreak(byte value) => value is (byte)'\r' or (byte)'\n';
    private static bool IsWhite(byte value) => value is 0 or 9 or 10 or 12 or 13 or 32;
    private static bool IsDelimiter(byte value) => IsWhite(value) || value is (byte)'/' or (byte)'<' or (byte)'>'
        or (byte)'(' or (byte)')' or (byte)'[' or (byte)']' or (byte)'%' or (byte)'{' or (byte)'}';
}
