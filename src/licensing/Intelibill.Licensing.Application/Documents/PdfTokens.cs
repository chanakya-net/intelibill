using System.Globalization;
using System.Text;

namespace Intelibill.Licensing.Application.Documents;

internal readonly record struct PdfRange(int Start, int Length)
{
    public ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> content) => content.Slice(Start, Length);
}

internal readonly record struct PdfRef(int Number, int Generation);

internal static class PdfTokens
{
    private const int MaxDictEntries = 512;
    private const int MaxNesting = 16;

    public static bool IsLineBreak(byte value) => value is (byte)'\r' or (byte)'\n';
    public static bool IsWhite(byte value) => value is 0 or 9 or 10 or 12 or 13 or 32;
    public static bool IsDelimiter(byte value) => IsWhite(value) || value is (byte)'/' or (byte)'<' or (byte)'>'
        or (byte)'(' or (byte)')' or (byte)'[' or (byte)']' or (byte)'%' or (byte)'{' or (byte)'}';

    public static ReadOnlySpan<byte> TrimStart(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty && IsWhite(bytes[0])) bytes = bytes[1..];
        return bytes;
    }

    public static ReadOnlySpan<byte> TrimEnd(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty && IsWhite(bytes[^1])) bytes = bytes[..^1];
        return bytes;
    }

    public static void SkipSpaceAndComments(ReadOnlySpan<byte> content, ref int pos)
    {
        while (pos < content.Length)
        {
            if (IsWhite(content[pos])) { pos++; continue; }
            if (content[pos] != (byte)'%') break;
            while (pos < content.Length && !IsLineBreak(content[pos])) pos++;
        }
    }

    public static bool TryConsumeKeyword(ReadOnlySpan<byte> content, ref int pos, ReadOnlySpan<byte> keyword)
    {
        if (pos + keyword.Length > content.Length || !content.Slice(pos, keyword.Length).SequenceEqual(keyword))
            return false;
        var after = pos + keyword.Length;
        if (after < content.Length && !IsDelimiter(content[after])) return false;
        pos = after;
        return true;
    }

    public static bool TryReadToken(ReadOnlySpan<byte> content, ref int pos, out ReadOnlySpan<byte> token)
    {
        SkipSpaceAndComments(content, ref pos);
        var start = pos;
        while (pos < content.Length && !IsDelimiter(content[pos])) pos++;
        token = content[start..pos];
        return !token.IsEmpty;
    }

    public static bool TryReadObjectHeader(ReadOnlySpan<byte> content, int offset, out int number, out int generation, out int afterObj)
    {
        number = generation = afterObj = 0;
        if (offset < 9 || offset >= content.Length || !IsDelimiter(content[offset - 1])) return false;
        var pos = offset;
        var skipped = 0;
        while (pos < content.Length && IsWhite(content[pos]) && skipped < 4) { pos++; skipped++; }
        if (!TryReadToken(content, ref pos, out var numToken) || !TryNumber(numToken, out number) || number < 1) return false;
        if (!TryReadToken(content, ref pos, out var genToken) || !TryNumber(genToken, out generation) || generation > 65535) return false;
        SkipSpaceAndComments(content, ref pos);
        if (!TryConsumeKeyword(content, ref pos, "obj"u8)) return false;
        afterObj = pos;
        return true;
    }

    public static bool TryParseDictionary(ReadOnlySpan<byte> content, ref int pos, out Dictionary<string, PdfRange> entries)
    {
        entries = new Dictionary<string, PdfRange>(StringComparer.Ordinal);
        SkipSpaceAndComments(content, ref pos);
        if (pos + 2 > content.Length || content[pos] != (byte)'<' || content[pos + 1] != (byte)'<') return false;
        pos += 2;
        while (entries.Count <= MaxDictEntries)
        {
            SkipSpaceAndComments(content, ref pos);
            if (pos + 2 <= content.Length && content[pos] == (byte)'>' && content[pos + 1] == (byte)'>')
            {
                pos += 2;
                return true;
            }
            if (!TryReadName(content, ref pos, out var key)) return false;
            SkipSpaceAndComments(content, ref pos);
            var valueStart = pos;
            if (!TrySkipValue(content, ref pos, 0)) return false;
            entries[Encoding.Latin1.GetString(key)] = new PdfRange(valueStart, pos - valueStart);
        }
        return false;
    }

    public static bool TryReadName(ReadOnlySpan<byte> content, ref int pos, out ReadOnlySpan<byte> name)
    {
        name = default;
        if (pos >= content.Length || content[pos] != (byte)'/') return false;
        var start = pos++;
        while (pos < content.Length && !IsDelimiter(content[pos])) pos++;
        name = content[start..pos];
        return name.Length > 1;
    }

    public static bool TryParseInt(ReadOnlySpan<byte> value, out int number) =>
        TryNumber(TrimEnd(TrimStart(value)), out number);

    public static bool TryParseIndirectRef(ReadOnlySpan<byte> value, out PdfRef reference)
    {
        reference = default;
        var pos = 0;
        if (!TryReadToken(value, ref pos, out var numToken) || !TryNumber(numToken, out var number) || number < 1) return false;
        if (!TryReadToken(value, ref pos, out var genToken) || !TryNumber(genToken, out var gen) || gen > 65535) return false;
        SkipSpaceAndComments(value, ref pos);
        if (!TryConsumeKeyword(value, ref pos, "R"u8)) return false;
        SkipSpaceAndComments(value, ref pos);
        if (pos != value.Length) return false;
        reference = new PdfRef(number, gen);
        return true;
    }

    public static bool TryParseIntArray(ReadOnlySpan<byte> value, int maxItems, out List<int> items)
    {
        items = [];
        var pos = 0;
        SkipSpaceAndComments(value, ref pos);
        if (pos >= value.Length || value[pos] != (byte)'[') return false;
        pos++;
        while (items.Count <= maxItems)
        {
            SkipSpaceAndComments(value, ref pos);
            if (pos < value.Length && value[pos] == (byte)']') { pos++; SkipSpaceAndComments(value, ref pos); return pos == value.Length; }
            if (!TryReadToken(value, ref pos, out var token) || !TryNumber(token, out var number)) return false;
            items.Add(number);
        }
        return false;
    }

    // Reads every reference of an array; more than maxItems is a refusal, never a silent truncation.
    public static bool TryParseIndirectRefArray(ReadOnlySpan<byte> value, int maxItems, out List<PdfRef> refs)
    {
        refs = [];
        if (!TryParseArrayItems(value, maxItems, out var items)) return false;
        foreach (var item in items)
        {
            if (!TryParseIndirectRef(item.Slice(value), out var reference)) return false;
            refs.Add(reference);
        }
        return true;
    }

    public static bool TryParseArrayItems(ReadOnlySpan<byte> value, int maxItems, out List<PdfRange> items)
    {
        items = [];
        var pos = 0;
        SkipSpaceAndComments(value, ref pos);
        if (pos >= value.Length || value[pos] != (byte)'[') return false;
        pos++;
        while (items.Count <= maxItems)
        {
            SkipSpaceAndComments(value, ref pos);
            if (pos < value.Length && value[pos] == (byte)']')
            {
                pos++;
                SkipSpaceAndComments(value, ref pos);
                return pos == value.Length;
            }
            if (items.Count == maxItems || !TryReadValue(value, ref pos, out var item)) return false;
            items.Add(item);
        }
        return false;
    }

    public static bool TryReadValue(ReadOnlySpan<byte> content, ref int pos, out PdfRange value)
    {
        SkipSpaceAndComments(content, ref pos);
        var start = pos;
        var ok = TrySkipValue(content, ref pos, 0);
        value = new PdfRange(start, pos - start);
        return ok && value.Length > 0;
    }

    public static bool TryConsumeStream(ReadOnlySpan<byte> content, int afterDict, int length,
        out ReadOnlySpan<byte> streamBytes, out int afterEndObj)
    {
        streamBytes = default;
        afterEndObj = 0;
        if (length < 0) return false;
        var pos = afterDict;
        SkipSpaceAndComments(content, ref pos);
        if (pos + 6 >= content.Length || !content.Slice(pos, 6).SequenceEqual("stream"u8)) return false;
        pos += 6;
        if (content[pos] == (byte)'\r' && pos + 1 < content.Length && content[pos + 1] == (byte)'\n') pos += 2;
        else if (content[pos] == (byte)'\n') pos += 1;
        else return false;

        if (length > content.Length - pos) return false;
        streamBytes = content.Slice(pos, length);
        pos += length;
        if (pos < content.Length && content[pos] == (byte)'\r') pos += (pos + 1 < content.Length && content[pos + 1] == (byte)'\n') ? 2 : 1;
        else if (pos < content.Length && content[pos] == (byte)'\n') pos += 1;

        if (!TryConsumeKeyword(content, ref pos, "endstream"u8)) return false;
        SkipSpaceAndComments(content, ref pos);
        if (!TryConsumeKeyword(content, ref pos, "endobj"u8)) return false;
        afterEndObj = pos;
        return true;
    }

    public static bool TryNumber(ReadOnlySpan<byte> token, out int number) =>
        int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out number);

    private static bool TrySkipValue(ReadOnlySpan<byte> content, ref int pos, int depth)
    {
        if (depth > MaxNesting || pos >= content.Length) return false;
        return content[pos] switch
        {
            (byte)'<' when pos + 1 < content.Length && content[pos + 1] == (byte)'<' => TrySkipComposite(content, ref pos, depth, isDict: true),
            (byte)'<' => TrySkipHexString(content, ref pos),
            (byte)'(' => TrySkipLiteralString(content, ref pos),
            (byte)'[' => TrySkipComposite(content, ref pos, depth, isDict: false),
            (byte)'/' => TryReadName(content, ref pos, out _),
            _ => TrySkipScalarOrRef(content, ref pos),
        };
    }

    private static bool TrySkipComposite(ReadOnlySpan<byte> content, ref int pos, int depth, bool isDict)
    {
        pos += isDict ? 2 : 1;
        while (pos < content.Length)
        {
            SkipSpaceAndComments(content, ref pos);
            if (isDict && pos + 2 <= content.Length && content[pos] == (byte)'>' && content[pos + 1] == (byte)'>') { pos += 2; return true; }
            if (!isDict && pos < content.Length && content[pos] == (byte)']') { pos++; return true; }
            if (!TrySkipValue(content, ref pos, depth + 1)) return false;
        }
        return false;
    }

    private static bool TrySkipHexString(ReadOnlySpan<byte> content, ref int pos)
    {
        pos++;
        while (pos < content.Length && content[pos] != (byte)'>') pos++;
        if (pos >= content.Length) return false;
        pos++;
        return true;
    }

    private static bool TrySkipLiteralString(ReadOnlySpan<byte> content, ref int pos)
    {
        pos++;
        var depth = 1;
        while (pos < content.Length && depth > 0)
        {
            var current = content[pos++];
            if (current == (byte)'\\' && pos < content.Length) { pos++; continue; }
            if (current == (byte)'(') depth++;
            else if (current == (byte)')') depth--;
        }
        return depth == 0;
    }

    private static bool TrySkipScalarOrRef(ReadOnlySpan<byte> content, ref int pos)
    {
        if (!TryReadToken(content, ref pos, out var first)) return false;
        if (!TryNumber(first, out _)) return true;
        var saved = pos;
        if (TryReadToken(content, ref pos, out var second) && TryNumber(second, out _))
        {
            SkipSpaceAndComments(content, ref pos);
            if (TryConsumeKeyword(content, ref pos, "R"u8)) return true;
        }
        pos = saved;
        return true;
    }
}
