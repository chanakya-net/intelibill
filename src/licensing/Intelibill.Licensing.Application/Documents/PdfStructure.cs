namespace Intelibill.Licensing.Application.Documents;

// A structural envelope check only. The application never interprets or renders document objects.
internal static class PdfStructure
{
    private const int MaxSections = 32;
    private const int MaxEntries = 200_000;

    public static bool HasEnvelope(ReadOnlySpan<byte> content)
    {
        if (!HasHeader(content)) return false;
        content = PdfTokens.TrimEnd(content);
        if (!content.EndsWith("%%EOF"u8) || !PdfTokens.IsLineBreak(content[^6])) return false;
        if (!TryReadStartXRef(content, out var firstOffset)) return false;

        var entries = new Dictionary<int, XRefEntry>();
        if (!TryWalkXRefChain(content, firstOffset, entries, out var root)) return false;
        return root is { } rootRef && ValidateDocumentSpine(content, new PdfObjects(entries), rootRef);
    }

    private static bool HasHeader(ReadOnlySpan<byte> content) =>
        content.Length >= 32 && content.StartsWith("%PDF-"u8) && content[6] == '.' && PdfTokens.IsLineBreak(content[8])
        && (content[5] == '1' && content[7] is >= (byte)'0' and <= (byte)'7' || content[5] == '2' && content[7] == '0');

    private static bool TryReadStartXRef(ReadOnlySpan<byte> content, out int offset)
    {
        offset = 0;
        var tailStart = Math.Max(9, content.Length - 1024);
        var marker = content[tailStart..^5].LastIndexOf("startxref"u8);
        if (marker < 0) return false;
        marker += tailStart;
        if (!PdfTokens.IsLineBreak(content[marker - 1]) || !PdfTokens.IsWhite(content[marker + 9])) return false;

        var pointer = PdfTokens.TrimEnd(PdfTokens.TrimStart(content[(marker + 9)..^5]));
        return PdfTokens.TryNumber(pointer, out offset) && offset >= 9 && offset < marker && PdfTokens.IsWhite(content[offset - 1]);
    }

    private static bool TryWalkXRefChain(ReadOnlySpan<byte> content, int firstOffset,
        Dictionary<int, XRefEntry> entries, out PdfRef? root)
    {
        root = null;
        var visited = new HashSet<int>();
        var queue = new Queue<(int Offset, bool StreamOnly)>();
        queue.Enqueue((firstOffset, false));
        while (queue.Count > 0)
        {
            if (visited.Count >= MaxSections) return false;
            var (offset, streamOnly) = queue.Dequeue();
            if (offset < 9 || offset >= content.Length || !visited.Add(offset)) return false;

            var isTable = !streamOnly && IsClassicTable(content, offset);
            var ok = isTable
                ? TryReadTable(content, offset, entries, out var info)
                : PdfXRefStream.TryRead(content, offset, entries, MaxEntries, out info);
            if (!ok) return false;

            root ??= info.Root;
            if (info.XRefStm is { } stm) queue.Enqueue((stm, true));
            if (info.Prev is { } prev) queue.Enqueue((prev, false));
        }
        return visited.Count > 0 && entries.Count > 0;
    }

    private static bool IsClassicTable(ReadOnlySpan<byte> content, int offset) =>
        offset + 4 < content.Length && content.Slice(offset, 4).SequenceEqual("xref"u8) && PdfTokens.IsWhite(content[offset + 4]);

    private static bool TryReadTable(ReadOnlySpan<byte> content, int offset, Dictionary<int, XRefEntry> entries,
        out XRefSectionInfo info)
    {
        info = default;
        var pos = offset + 4;
        var maxEntryEnd = 0;
        var subsections = 0;
        while (true)
        {
            PdfTokens.SkipSpaceAndComments(content, ref pos);
            if (PdfTokens.TryConsumeKeyword(content, ref pos, "trailer"u8)) break;
            if (!PdfTokens.TryReadToken(content, ref pos, out var firstToken) || !PdfTokens.TryNumber(firstToken, out var first)
                || !PdfTokens.TryReadToken(content, ref pos, out var countToken) || !PdfTokens.TryNumber(countToken, out var count)
                || first > int.MaxValue - count || count > MaxEntries - entries.Count) return false;
            subsections++;
            maxEntryEnd = Math.Max(maxEntryEnd, first + count);
            for (var i = 0; i < count; i++)
            {
                if (!TryReadTableEntry(content, ref pos, out var entry)) return false;
                entries.TryAdd(first + i, entry);
            }
        }
        if (subsections == 0 || !PdfTokens.TryParseDictionary(content, ref pos, out var trailer)) return false;
        if (!trailer.TryGetValue("/Size", out var sizeRange) || !PdfTokens.TryParseInt(sizeRange.Slice(content), out var size)
            || size < 1 || maxEntryEnd > size) return false;
        return TryReadTrailerLinks(content, trailer, out info);
    }

    private static bool TryReadTableEntry(ReadOnlySpan<byte> content, ref int pos, out XRefEntry entry)
    {
        entry = default;
        if (!PdfTokens.TryReadToken(content, ref pos, out var position) || position.Length != 10
            || !PdfTokens.TryReadToken(content, ref pos, out var generationToken) || generationToken.Length != 5
            || !PdfTokens.TryReadToken(content, ref pos, out var flag)
            || !PdfTokens.TryNumber(generationToken, out var generation) || generation > 65535
            || !PdfTokens.TryNumber(position, out var number)) return false;
        if (flag is [(byte)'f']) { entry = new XRefEntry(0, number, generation); return true; }
        if (flag is not [(byte)'n'] || number < 9 || number >= content.Length) return false;
        entry = new XRefEntry(1, number, generation);
        return true;
    }

    private static bool TryReadTrailerLinks(ReadOnlySpan<byte> content, Dictionary<string, PdfRange> trailer, out XRefSectionInfo info)
    {
        info = default;
        PdfRef? root = null;
        int? prev = null;
        int? xrefStm = null;
        if (trailer.TryGetValue("/Root", out var rootRange))
        {
            if (!PdfTokens.TryParseIndirectRef(rootRange.Slice(content), out var parsed)) return false;
            root = parsed;
        }
        if (trailer.TryGetValue("/Prev", out var prevRange))
        {
            if (!PdfTokens.TryParseInt(prevRange.Slice(content), out var parsed) || parsed < 9) return false;
            prev = parsed;
        }
        if (trailer.TryGetValue("/XRefStm", out var stmRange))
        {
            if (!PdfTokens.TryParseInt(stmRange.Slice(content), out var parsed) || parsed < 9) return false;
            xrefStm = parsed;
        }
        info = new XRefSectionInfo(root, prev, xrefStm);
        return true;
    }

    private static bool ValidateDocumentSpine(ReadOnlySpan<byte> content, PdfObjects objects, PdfRef rootRef)
    {
        if (!objects.TryResolveDictionary(content, rootRef, out var value, out var catalog)) return false;
        var source = value.Source(content);
        return catalog.TryGetValue("/Type", out var type) && type.Slice(source).SequenceEqual("/Catalog"u8)
            && catalog.TryGetValue("/Pages", out var pages) && PdfTokens.TryParseIndirectRef(pages.Slice(source), out var pagesRef)
            && PdfPageTree.IsValid(content, objects, pagesRef);
    }
}
