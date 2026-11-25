using System.IO.Compression;

namespace Intelibill.Licensing.Application.Documents;

internal readonly record struct XRefEntry(byte Type, int Field2, int Field3);

internal readonly record struct XRefSectionInfo(PdfRef? Root, int? Prev, int? XRefStm);

internal static class PdfXRefStream
{
    private const int MaxDecodedBytes = 4 * 1024 * 1024;

    public static bool TryRead(ReadOnlySpan<byte> content, int offset, Dictionary<int, XRefEntry> entries,
        int maxEntries, out XRefSectionInfo info)
    {
        info = default;
        if (!PdfTokens.TryReadObjectHeader(content, offset, out _, out _, out var pos)) return false;
        if (!PdfTokens.TryParseDictionary(content, ref pos, out var dict)) return false;
        if (!HasTypeXRef(content, dict) || !TryReadInt(content, dict, "/Size", out var size) || size < 1) return false;
        if (!TryReadInt(content, dict, "/Length", out var length) || length < 0) return false;
        if (!TryReadWidths(content, dict, out var w0, out var w1, out var w2)) return false;
        if (!TryReadIndex(content, dict, size, maxEntries - entries.Count, out var ranges, out var totalCount)) return false;
        if (!TryReadOptionalRefs(content, dict, out info)) return false;
        if (!PdfTokens.TryConsumeStream(content, pos, length, out var rawStream, out _)) return false;

        var entryWidth = w0 + w1 + w2;
        if (totalCount > MaxDecodedBytes / entryWidth) return false;
        var expectedDecoded = totalCount * entryWidth;
        if (!TryDecodePayload(content, dict, rawStream, expectedDecoded, entryWidth, out var decoded)) return false;
        return ParseEntries(decoded, ranges, w0, w1, w2, content.Length, entries);
    }

    private static bool HasTypeXRef(ReadOnlySpan<byte> content, Dictionary<string, PdfRange> dict) =>
        dict.TryGetValue("/Type", out var range) && range.Slice(content).SequenceEqual("/XRef"u8);

    private static bool TryReadInt(ReadOnlySpan<byte> content, Dictionary<string, PdfRange> dict, string key, out int value)
    {
        value = 0;
        return dict.TryGetValue(key, out var range) && PdfTokens.TryParseInt(range.Slice(content), out value);
    }

    private static bool TryReadWidths(ReadOnlySpan<byte> content, Dictionary<string, PdfRange> dict,
        out int w0, out int w1, out int w2)
    {
        w0 = w1 = w2 = 0;
        if (!dict.TryGetValue("/W", out var range) || !PdfTokens.TryParseIntArray(range.Slice(content), 3, out var widths)
            || widths.Count != 3) return false;
        (w0, w1, w2) = (widths[0], widths[1], widths[2]);
        return w0 <= 4 && w1 <= 8 && w2 <= 4 && w0 + w1 + w2 is >= 1 and <= 16;
    }

    private static bool TryReadIndex(ReadOnlySpan<byte> content, Dictionary<string, PdfRange> dict, int size,
        int remainingCapacity, out List<(int First, int Count)> ranges, out int totalCount)
    {
        ranges = [];
        totalCount = 0;
        if (!dict.TryGetValue("/Index", out var range))
        {
            if (size > remainingCapacity) return false;
            ranges.Add((0, size));
            totalCount = size;
            return true;
        }
        if (!PdfTokens.TryParseIntArray(range.Slice(content), 128, out var values) || values.Count < 2 || values.Count % 2 != 0)
            return false;
        for (var i = 0; i < values.Count; i += 2)
        {
            var (first, count) = (values[i], values[i + 1]);
            if (first > size - count || count > remainingCapacity - totalCount) return false;
            ranges.Add((first, count));
            totalCount += count;
        }
        return totalCount > 0;
    }

    private static bool TryReadOptionalRefs(ReadOnlySpan<byte> content, Dictionary<string, PdfRange> dict, out XRefSectionInfo info)
    {
        info = default;
        PdfRef? root = null;
        int? prev = null;
        if (dict.TryGetValue("/Root", out var rootRange))
        {
            if (!PdfTokens.TryParseIndirectRef(rootRange.Slice(content), out var parsed)) return false;
            root = parsed;
        }
        if (dict.TryGetValue("/Prev", out var prevRange))
        {
            if (!PdfTokens.TryParseInt(prevRange.Slice(content), out var parsedPrev) || parsedPrev < 9) return false;
            prev = parsedPrev;
        }
        info = new XRefSectionInfo(root, prev, null);
        return true;
    }

    private static bool TryDecodePayload(ReadOnlySpan<byte> content, Dictionary<string, PdfRange> dict,
        ReadOnlySpan<byte> rawStream, int expectedDecoded, int entryWidth, out byte[] decoded)
    {
        decoded = [];
        if (!TryCheckFilter(content, dict, out var flate)) return false;
        if (!flate)
        {
            if (rawStream.Length != expectedDecoded) return false;
            decoded = rawStream.ToArray();
            return true;
        }
        if (!TryReadPredictor(content, dict, entryWidth, out var predictor, out var columns, out var bytesPerPixel))
            return false;
        var rowWidth = columns * bytesPerPixel;
        if (expectedDecoded % rowWidth != 0) return false;
        var rows = expectedDecoded / rowWidth;
        var encodedSize = predictor >= 10 ? rows * (rowWidth + 1) : expectedDecoded;
        if (encodedSize > MaxDecodedBytes || !TryDecompressZlib(rawStream, encodedSize, out var inflated)) return false;
        if (predictor == 1) { decoded = inflated; return true; }
        return predictor == 2
            ? TryUndoTiffPredictor(inflated, rows, rowWidth, bytesPerPixel, out decoded)
            : TryUndoPngPredictor(inflated, rows, rowWidth, bytesPerPixel, out decoded);
    }

    private static bool TryCheckFilter(ReadOnlySpan<byte> content, Dictionary<string, PdfRange> dict, out bool flate)
    {
        flate = false;
        if (!dict.TryGetValue("/Filter", out var range)) return true;
        var value = PdfTokens.TrimEnd(PdfTokens.TrimStart(range.Slice(content)));
        if (value.SequenceEqual("/FlateDecode"u8)) { flate = true; return true; }
        if (value.Length > 2 && value[0] == (byte)'[' && value[^1] == (byte)']'
            && PdfTokens.TrimEnd(PdfTokens.TrimStart(value[1..^1])).SequenceEqual("/FlateDecode"u8))
        {
            flate = true;
            return true;
        }
        return false;
    }

    private static bool TryReadPredictor(ReadOnlySpan<byte> content, Dictionary<string, PdfRange> dict,
        int defaultColumns, out int predictor, out int columns, out int bytesPerPixel)
    {
        predictor = 1;
        columns = defaultColumns;
        bytesPerPixel = 1;
        if (!dict.TryGetValue("/DecodeParms", out var range)) return true;
        var slice = PdfTokens.TrimEnd(PdfTokens.TrimStart(range.Slice(content)));
        if (slice.SequenceEqual("null"u8)) return true;
        var pos = 0;
        if (slice.Length > 2 && slice[0] == (byte)'[' && slice[^1] == (byte)']')
        {
            slice = PdfTokens.TrimEnd(PdfTokens.TrimStart(slice[1..^1]));
            if (slice.SequenceEqual("null"u8)) return true;
        }
        if (!PdfTokens.TryParseDictionary(slice, ref pos, out var parms)) return false;
        if (parms.TryGetValue("/Predictor", out var p) && !PdfTokens.TryParseInt(p.Slice(slice), out predictor)) return false;
        if (parms.TryGetValue("/Columns", out var c) && !PdfTokens.TryParseInt(c.Slice(slice), out columns)) return false;
        var colors = 1;
        var bpc = 8;
        if (parms.TryGetValue("/Colors", out var cl) && !PdfTokens.TryParseInt(cl.Slice(slice), out colors)) return false;
        if (parms.TryGetValue("/BitsPerComponent", out var bp) && !PdfTokens.TryParseInt(bp.Slice(slice), out bpc)) return false;
        bytesPerPixel = colors;
        return bpc == 8 && colors is >= 1 and <= 4 && columns is >= 1 and <= 64
            && predictor is 1 or 2 or (>= 10 and <= 15);
    }

    private static bool TryDecompressZlib(ReadOnlySpan<byte> rawStream, int expectedLength, out byte[] output)
    {
        output = new byte[expectedLength];
        try
        {
            using var input = new MemoryStream(rawStream.ToArray(), writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            var total = 0;
            while (total < expectedLength)
            {
                var read = zlib.Read(output, total, expectedLength - total);
                if (read == 0) return false;
                total += read;
            }
            Span<byte> extra = stackalloc byte[1];
            return zlib.Read(extra) == 0;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool TryUndoTiffPredictor(byte[] inflated, int rows, int rowWidth, int bpp, out byte[] decoded)
    {
        decoded = new byte[rows * rowWidth];
        for (var r = 0; r < rows; r++)
        {
            var offset = r * rowWidth;
            for (var i = 0; i < rowWidth; i++)
                decoded[offset + i] = i < bpp ? inflated[offset + i] : (byte)(inflated[offset + i] + decoded[offset + i - bpp]);
        }
        return true;
    }

    private static bool TryUndoPngPredictor(byte[] inflated, int rows, int rowWidth, int bpp, out byte[] decoded)
    {
        decoded = new byte[rows * rowWidth];
        for (var r = 0; r < rows; r++)
        {
            var src = r * (rowWidth + 1);
            var dst = r * rowWidth;
            var filter = inflated[src];
            if (filter > 4) return false;
            for (var i = 0; i < rowWidth; i++)
            {
                var raw = inflated[src + 1 + i];
                var left = i >= bpp ? decoded[dst + i - bpp] : (byte)0;
                var up = r > 0 ? decoded[dst - rowWidth + i] : (byte)0;
                var upLeft = r > 0 && i >= bpp ? decoded[dst - rowWidth + i - bpp] : (byte)0;
                decoded[dst + i] = filter switch
                {
                    0 => raw,
                    1 => (byte)(raw + left),
                    2 => (byte)(raw + up),
                    3 => (byte)(raw + ((left + up) >> 1)),
                    _ => (byte)(raw + Paeth(left, up, upLeft)),
                };
            }
        }
        return true;
    }

    private static byte Paeth(byte a, byte b, byte c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static bool ParseEntries(ReadOnlySpan<byte> decoded, List<(int First, int Count)> ranges,
        int w0, int w1, int w2, int fileLength, Dictionary<int, XRefEntry> entries)
    {
        var width = w0 + w1 + w2;
        var cursor = 0;
        foreach (var (first, count) in ranges)
        {
            for (var i = 0; i < count; i++)
            {
                var row = decoded.Slice(cursor, width);
                cursor += width;
                var type = w0 == 0 ? 1L : ReadBigEndian(row[..w0]);
                var f2 = ReadBigEndian(row.Slice(w0, w1));
                var f3 = ReadBigEndian(row.Slice(w0 + w1, w2));
                if (type > 2 || f2 > int.MaxValue || f3 > int.MaxValue) return false;
                if (type == 1 && (f2 < 9 || f2 >= fileLength || f3 > 65535)) return false;
                if (type == 2 && f2 < 1) return false;
                entries.TryAdd(first + i, new XRefEntry((byte)type, (int)f2, (int)f3));
            }
        }
        return true;
    }

    private static long ReadBigEndian(ReadOnlySpan<byte> bytes)
    {
        long value = 0;
        foreach (var b in bytes) value = (value << 8) | b;
        return value;
    }
}
