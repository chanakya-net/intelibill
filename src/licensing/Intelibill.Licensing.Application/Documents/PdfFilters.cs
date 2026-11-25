using System.IO.Compression;

namespace Intelibill.Licensing.Application.Documents;

// Bounded decoding of the two filters a structural stream (cross-reference or object stream) may
// legally use. Anything else, and any output past the caller's budget, is a refusal.
internal static class PdfFilters
{
    private const int MaxFilters = 4;

    public static bool TryDecode(ReadOnlySpan<byte> source, Dictionary<string, PdfRange> dict, ReadOnlySpan<byte> raw,
        int maxDecoded, int defaultColumns, out byte[] decoded)
    {
        decoded = [];
        if (!TryReadEntries(source, dict, "/Filter", out var filters)
            || !TryReadEntries(source, dict, "/DecodeParms", out var parms)
            || parms.Count > filters.Count) return false;

        var stageLimit = 2L * maxDecoded + 4096;
        var current = raw;
        byte[] buffer = [];
        for (var i = 0; i < filters.Count; i++)
        {
            var name = filters[i].Slice(source);
            var parm = i < parms.Count ? parms[i].Slice(source) : default;
            var ok = name.SequenceEqual("/FlateDecode"u8) ? TryInflate(current, parm, stageLimit, defaultColumns, out buffer)
                : name.SequenceEqual("/ASCIIHexDecode"u8) && TryDecodeHex(current, stageLimit, out buffer);
            if (!ok) return false;
            current = buffer;
        }
        if (current.Length > maxDecoded) return false;
        decoded = filters.Count == 0 ? raw.ToArray() : buffer;
        return true;
    }

    // A single value or an array of values; absent and null read as empty.
    private static bool TryReadEntries(ReadOnlySpan<byte> source, Dictionary<string, PdfRange> dict, string key,
        out List<PdfRange> entries)
    {
        entries = [];
        if (!dict.TryGetValue(key, out var range)) return true;
        var value = range.Slice(source);
        if (value.SequenceEqual("null"u8)) return true;
        if (value[0] != (byte)'[') { entries.Add(range); return true; }
        if (!PdfTokens.TryParseArrayItems(value, MaxFilters, out var items)) return false;
        foreach (var item in items) entries.Add(new PdfRange(range.Start + item.Start, item.Length));
        return true;
    }

    private static bool TryDecodeHex(ReadOnlySpan<byte> encoded, long limit, out byte[] decoded)
    {
        decoded = [];
        var output = new List<byte>(Math.Min(encoded.Length / 2 + 1, 4096));
        var high = -1;
        var index = 0;
        for (; index < encoded.Length && encoded[index] != (byte)'>'; index++)
        {
            if (PdfTokens.IsWhite(encoded[index])) continue;
            var digit = HexValue(encoded[index]);
            if (digit < 0) return false;
            if (high < 0) { high = digit; continue; }
            if (output.Count >= limit) return false;
            output.Add((byte)(high << 4 | digit));
            high = -1;
        }
        if (index < encoded.Length && !PdfTokens.TrimStart(encoded[(index + 1)..]).IsEmpty) return false;
        if (high >= 0) output.Add((byte)(high << 4));
        decoded = [.. output];
        return true;
    }

    private static int HexValue(byte value) => value switch
    {
        >= (byte)'0' and <= (byte)'9' => value - '0',
        >= (byte)'a' and <= (byte)'f' => value - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => value - 'A' + 10,
        _ => -1,
    };

    private static bool TryInflate(ReadOnlySpan<byte> encoded, ReadOnlySpan<byte> parm, long limit, int defaultColumns,
        out byte[] decoded)
    {
        decoded = [];
        if (!TryReadPredictor(parm, defaultColumns, out var predictor, out var rowWidth, out var bytesPerPixel)
            || !TryDecompressZlib(encoded, limit, out var inflated)) return false;
        if (predictor == 1) { decoded = inflated; return true; }
        var encodedRow = predictor == 2 ? rowWidth : rowWidth + 1;
        if (inflated.Length % encodedRow != 0) return false;
        var rows = inflated.Length / encodedRow;
        return predictor == 2
            ? TryUndoTiffPredictor(inflated, rows, rowWidth, bytesPerPixel, out decoded)
            : TryUndoPngPredictor(inflated, rows, rowWidth, bytesPerPixel, out decoded);
    }

    private static bool TryReadPredictor(ReadOnlySpan<byte> parm, int defaultColumns, out int predictor,
        out int rowWidth, out int bytesPerPixel)
    {
        predictor = 1;
        var columns = rowWidth = defaultColumns;
        bytesPerPixel = 1;
        if (parm.IsEmpty || parm.SequenceEqual("null"u8)) return true;
        var pos = 0;
        if (!PdfTokens.TryParseDictionary(parm, ref pos, out var parms)) return false;
        var colors = 1;
        var bpc = 8;
        if (!TryReadOptionalInt(parm, parms, "/Predictor", ref predictor) || !TryReadOptionalInt(parm, parms, "/Columns", ref columns)
            || !TryReadOptionalInt(parm, parms, "/Colors", ref colors) || !TryReadOptionalInt(parm, parms, "/BitsPerComponent", ref bpc))
            return false;
        bytesPerPixel = colors;
        rowWidth = columns * colors;
        return bpc == 8 && colors is >= 1 and <= 4 && columns is >= 1 and <= 64
            && predictor is 1 or 2 or (>= 10 and <= 15);
    }

    private static bool TryReadOptionalInt(ReadOnlySpan<byte> source, Dictionary<string, PdfRange> dict, string key, ref int value) =>
        !dict.TryGetValue(key, out var range) || PdfTokens.TryParseInt(range.Slice(source), out value);

    private static bool TryDecompressZlib(ReadOnlySpan<byte> encoded, long limit, out byte[] output)
    {
        output = [];
        try
        {
            using var input = new MemoryStream(encoded.ToArray(), writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var inflated = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = zlib.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (inflated.Length + read > limit) return false;
                inflated.Write(chunk, 0, read);
            }
            output = inflated.ToArray();
            return true;
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
}
