namespace Intelibill.Licensing.Application.Documents;

// Where a resolved object's value lives: the uploaded file (Buffer null) or a decoded object stream.
internal readonly record struct PdfValue(byte[]? Buffer, PdfRange Range)
{
    public ReadOnlySpan<byte> Source(ReadOnlySpan<byte> content) => Buffer is { } buffer ? buffer : content;
}

// One resolution rule for every indirect reference the structural check follows: a directly stored
// object and a member of an object stream get the same identity check and yield the same value shape.
// Exhausting a budget is a refusal; nothing required is ever approved unread.
internal sealed class PdfObjects(Dictionary<int, XRefEntry> entries)
{
    private const int MaxObjectStreams = 64;
    private const int MaxMembers = 100_000;
    private const int MaxStreamBytes = 8 * 1024 * 1024;
    private const int MaxTotalBytes = 32 * 1024 * 1024;

    private readonly Dictionary<int, ObjectStream?> streams = [];
    private int remainingBytes = MaxTotalBytes;

    private sealed record ObjectStream(byte[] Data, int First, int[] Numbers, int[] Offsets);

    public bool TryResolveDictionary(ReadOnlySpan<byte> content, PdfRef reference, out PdfValue value,
        out Dictionary<string, PdfRange> dictionary)
    {
        dictionary = default!;
        if (!TryResolve(content, reference, out value)) return false;
        var pos = value.Range.Start;
        return PdfTokens.TryParseDictionary(value.Source(content), ref pos, out dictionary)
            && pos == value.Range.Start + value.Range.Length;
    }

    public bool TryResolve(ReadOnlySpan<byte> content, PdfRef reference, out PdfValue value)
    {
        value = default;
        if (!entries.TryGetValue(reference.Number, out var entry)) return false;
        return entry.Type switch
        {
            1 => entry.Field3 == reference.Generation && TryReadDirect(content, entry.Field2, reference, out value),
            2 => reference.Generation == 0 && TryReadMember(content, entry.Field2, entry.Field3, reference.Number, out value),
            _ => false,
        };
    }

    private static bool TryReadDirect(ReadOnlySpan<byte> content, int offset, PdfRef expected, out PdfValue value)
    {
        value = default;
        if (!PdfTokens.TryReadObjectHeader(content, offset, out var number, out var generation, out var pos)
            || number != expected.Number || generation != expected.Generation) return false;
        if (!PdfTokens.TryReadValue(content, ref pos, out var range)) return false;
        PdfTokens.SkipSpaceAndComments(content, ref pos);
        if (!PdfTokens.TryConsumeKeyword(content, ref pos, "endobj"u8)) return false;
        value = new PdfValue(null, range);
        return true;
    }

    private bool TryReadMember(ReadOnlySpan<byte> content, int container, int index, int expectedNumber, out PdfValue value)
    {
        value = default;
        if (!streams.TryGetValue(container, out var stream))
        {
            if (streams.Count >= MaxObjectStreams) return false;
            stream = TryLoadStream(content, container, out var loaded) ? loaded : null;
            streams[container] = stream;
        }
        if (stream is null || index >= stream.Numbers.Length || stream.Numbers[index] != expectedNumber) return false;

        var pos = stream.First + stream.Offsets[index];
        var end = index + 1 < stream.Offsets.Length ? stream.First + stream.Offsets[index + 1] : stream.Data.Length;
        if (!PdfTokens.TryReadValue(stream.Data.AsSpan(0, end), ref pos, out var range)) return false;
        value = new PdfValue(stream.Data, range);
        return true;
    }

    private bool TryLoadStream(ReadOnlySpan<byte> content, int number, out ObjectStream stream)
    {
        stream = default!;
        if (!entries.TryGetValue(number, out var entry) || entry.Type != 1 || entry.Field3 != 0) return false;
        if (!PdfTokens.TryReadObjectHeader(content, entry.Field2, out var headerNumber, out var generation, out var pos)
            || headerNumber != number || generation != 0) return false;
        if (!PdfTokens.TryParseDictionary(content, ref pos, out var dict)) return false;
        if (!dict.TryGetValue("/Type", out var type) || !type.Slice(content).SequenceEqual("/ObjStm"u8)) return false;
        if (!TryReadInt(content, dict, "/N", out var count) || count < 1 || count > MaxMembers) return false;
        if (!TryReadInt(content, dict, "/First", out var first) || first < 0) return false;
        if (!dict.TryGetValue("/Length", out var lengthRange) || !TryResolveLength(content, lengthRange.Slice(content), out var length))
            return false;
        if (!PdfTokens.TryConsumeStream(content, pos, length, out var raw, out _)) return false;

        var budget = Math.Min(MaxStreamBytes, remainingBytes);
        if (!PdfFilters.TryDecode(content, dict, raw, budget, 1, out var data) || first > data.Length) return false;
        remainingBytes -= data.Length;
        if (!TryReadDirectory(data.AsSpan(0, first), count, data.Length - first, out var numbers, out var offsets)) return false;
        stream = new ObjectStream(data, first, numbers, offsets);
        return true;
    }

    // N pairs of object number and offset; offsets ascend and stay inside the stream's object area.
    private static bool TryReadDirectory(ReadOnlySpan<byte> header, int count, int bodyLength, out int[] numbers, out int[] offsets)
    {
        numbers = new int[count];
        offsets = new int[count];
        var pos = 0;
        var previous = -1;
        for (var i = 0; i < count; i++)
        {
            if (!PdfTokens.TryReadToken(header, ref pos, out var numberToken) || !PdfTokens.TryNumber(numberToken, out numbers[i])
                || !PdfTokens.TryReadToken(header, ref pos, out var offsetToken) || !PdfTokens.TryNumber(offsetToken, out offsets[i])
                || numbers[i] < 1 || offsets[i] <= previous || offsets[i] >= bodyLength) return false;
            previous = offsets[i];
        }
        return true;
    }

    private static bool TryReadInt(ReadOnlySpan<byte> content, Dictionary<string, PdfRange> dict, string key, out int value)
    {
        value = 0;
        return dict.TryGetValue(key, out var range) && PdfTokens.TryParseInt(range.Slice(content), out value);
    }

    // A stream length is a direct integer or a reference to a directly stored integer object.
    private bool TryResolveLength(ReadOnlySpan<byte> content, ReadOnlySpan<byte> value, out int length)
    {
        if (PdfTokens.TryParseInt(value, out length)) return true;
        length = 0;
        return PdfTokens.TryParseIndirectRef(value, out var reference)
            && entries.TryGetValue(reference.Number, out var entry) && entry.Type == 1
            && TryResolve(content, reference, out var resolved)
            && PdfTokens.TryNumber(resolved.Range.Slice(content), out length);
    }
}
