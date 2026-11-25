namespace Intelibill.Licensing.Application.Documents;

// Walks the whole page tree: every node is resolved, identified and typed, whichever way it is stored.
internal static class PdfPageTree
{
    private const int MaxNodes = 100_000;

    public static bool IsValid(ReadOnlySpan<byte> content, PdfObjects objects, PdfRef root)
    {
        var seen = new HashSet<int> { root.Number };
        var pending = new Stack<PdfRef>();
        pending.Push(root);
        var isRoot = true;
        while (pending.TryPop(out var node))
        {
            if (!objects.TryResolveDictionary(content, node, out var value, out var dictionary)) return false;
            var source = value.Source(content);
            if (!dictionary.TryGetValue("/Type", out var type)) return false;
            var isLeaf = type.Slice(source).SequenceEqual("/Page"u8);
            if (isLeaf && isRoot) return false;
            isRoot = false;
            if (isLeaf) continue;
            if (!type.Slice(source).SequenceEqual("/Pages"u8) || !dictionary.TryGetValue("/Kids", out var kidsRange)
                || !TryReadKids(content, objects, source, kidsRange, MaxNodes - seen.Count, out var kids)) return false;
            foreach (var kid in kids)
            {
                if (!seen.Add(kid.Number)) return false;
                pending.Push(kid);
            }
        }
        return true;
    }

    // /Kids is an array of references, or a reference to such an array.
    private static bool TryReadKids(ReadOnlySpan<byte> content, PdfObjects objects, ReadOnlySpan<byte> source,
        PdfRange kidsRange, int budget, out List<PdfRef> kids)
    {
        var slice = kidsRange.Slice(source);
        if (!PdfTokens.TryParseIndirectRef(slice, out var arrayRef))
            return PdfTokens.TryParseIndirectRefArray(slice, budget, out kids);
        kids = [];
        return objects.TryResolve(content, arrayRef, out var array)
            && PdfTokens.TryParseIndirectRefArray(array.Range.Slice(array.Source(content)), budget, out kids);
    }
}
