namespace Intelibill.Licensing.Application.Documents;

internal static class PdfPageTree
{
    private const int MaxNodes = 100_000;

    // A branch exit follows all its children on the depth-first walk.
    private readonly record struct Visit(PdfRef Node, int? ExpectedLeavesAtExit = null);

    public static bool IsValid(ReadOnlySpan<byte> content, PdfObjects objects, PdfRef root)
    {
        var seen = new HashSet<int> { root.Number };
        var pending = new Stack<Visit>();
        pending.Push(new Visit(root));
        var leaves = 0;
        while (pending.TryPop(out var visit))
        {
            if (visit.ExpectedLeavesAtExit is { } expected)
            {
                if (leaves != expected) return false;
                continue;
            }
            if (!objects.TryResolveDictionary(content, visit.Node, out var value, out var dictionary)
                || !dictionary.TryGetValue("/Type", out var type)) return false;
            var source = value.Source(content);
            if (type.Slice(source).SequenceEqual("/Page"u8))
            {
                if (visit.Node == root) return false;
                leaves++;
                continue;
            }

            if (!type.Slice(source).SequenceEqual("/Pages"u8)
                || !TryReadBranch(content, objects, source, dictionary, MaxNodes - seen.Count, out var count, out var kids))
                return false;
            pending.Push(new Visit(visit.Node, leaves + count));
            foreach (var kid in kids)
            {
                if (!seen.Add(kid.Number)) return false;
                pending.Push(new Visit(kid));
            }
        }
        return leaves > 0;
    }

    private static bool TryReadBranch(ReadOnlySpan<byte> content, PdfObjects objects, ReadOnlySpan<byte> source,
        Dictionary<string, PdfRange> dictionary, int budget, out int count, out List<PdfRef> kids)
    {
        kids = [];
        count = 0;
        if (!dictionary.TryGetValue("/Count", out var countRange)
            || !TryReadValue(content, objects, countRange.Slice(source), out var number)) return false;
        if (number.StartsWith("+"u8)) number = number[1..];
        return PdfTokens.TryParseInt(number, out count) && count <= MaxNodes
            && dictionary.TryGetValue("/Kids", out var kidsRange)
            && TryReadValue(content, objects, kidsRange.Slice(source), out var array)
            && PdfTokens.TryParseIndirectRefArray(array, budget, out kids);
    }

    private static bool TryReadValue(ReadOnlySpan<byte> content, PdfObjects objects, ReadOnlySpan<byte> entry,
        out ReadOnlySpan<byte> value)
    {
        value = entry;
        if (!PdfTokens.TryParseIndirectRef(entry, out var reference)) return true;
        if (!objects.TryResolve(content, reference, out var resolved)) return false;
        value = resolved.Range.Slice(resolved.Source(content));
        return true;
    }
}
