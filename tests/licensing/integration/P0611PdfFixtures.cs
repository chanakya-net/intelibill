using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Intelibill.Licensing.Integration.Tests;

// Builds small PDFs whose catalog and page tree live in a compressed object stream, plus classic
// equivalents, so the upload validator is exercised on both representations of the same references.
internal static class P0611PdfFixtures
{
    private const string Catalog = "<< /Type /Catalog /Pages 3 0 R >>";
    private const string Page = "<< /Type /Page /Parent 3 0 R /MediaBox [0 0 240 160] >>";
    private const string Font = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>";

    public static byte[] Valid(string scenario) => scenario switch
    {
        "ascii85-object" => Ascii85Fixture("object"),
        "ascii85-xref" => Ascii85Fixture("xref"),
        "classic-one-page" => ClassicOnePage("[3 0 R]"),
        "nested-indirect-count" => NestedPages("7 0 R"),
        "compressed-spine" => Compressed([Catalog, Pages("[4 0 R]", 1), Page]),
        "compressed-indirect-kids" => Compressed([Catalog, Pages("5 0 R", 1), Page, "[4 0 R]"]),
        "compressed-forty-pages" => Compressed(FortyPages(Catalog)),
        "classic-forty-pages" => Classic(FortyPages("<< /Type /Catalog /Pages 2 0 R >>", first: 3)),
        "ascii-hex-xref-name" => P0611Tests.AsciiHexCrossReferenceStreamPdf("/ASCIIHexDecode"),
        "ascii-hex-xref-array" => P0611Tests.AsciiHexCrossReferenceStreamPdf("[ /ASCIIHexDecode ]"),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    public static byte[] Damaged(string scenario) => scenario switch
    {
        "ascii85-xref-bad-end" => MissingAscii85End(),
        "classic-no-reachable-page" => ClassicOnePage("[]"),
        "compressed-no-reachable-page" => Compressed([Catalog, Pages("[ ]", 1), Page]),
        "indirect-kids-no-reachable-page" => Compressed([Catalog, Pages("5 0 R", 1), Page, "[]"]),
        "nested-count-mismatch" => NestedPages("2"),
        "compressed-root-wrong-type" => Compressed([Catalog, Pages("[4 0 R]", 1), Page], root: 4),
        "compressed-pages-wrong-type" => Compressed(["<< /Type /Catalog /Pages 4 0 R >>", Pages("[4 0 R]", 1), Page]),
        "compressed-member-identity" => Compressed([Catalog, Pages("[4 0 R]", 1), Page], directoryShift: 1),
        "compressed-member-unparsable" => Compressed(["<< /Type /Catalog /Pages 3 0 R", Pages("[4 0 R]", 1), Page]),
        "compressed-kid-wrong-type" => Compressed([Catalog, Pages("[4 0 R]", 1), Font]),
        "compressed-indirect-kids-wrong-type" => Compressed([Catalog, Pages("5 0 R", 1), Page, "[2 0 R]"]),
        "compressed-late-kid-wrong-type" => Compressed([.. FortyPages(Catalog)[..^1], Font]),
        "classic-late-kid-missing" => Classic(FortyPages("<< /Type /Catalog /Pages 2 0 R >>", first: 3, lastKid: 99)),
        "classic-late-kid-wrong-type" => Classic([.. FortyPages("<< /Type /Catalog /Pages 2 0 R >>", first: 3)[..^1], Font]),
        "ascii-hex-xref-malformed" => P0611Tests.AsciiHexCrossReferenceStreamPdf("/ASCIIHexDecode", hex => "ZZ" + hex[2..]),
        "ascii-hex-xref-short" => P0611Tests.AsciiHexCrossReferenceStreamPdf("/ASCIIHexDecode", hex => hex[14..]),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    private static byte[] Ascii85Fixture(string stream) =>
        File.ReadAllBytes(Path.Combine(ProjectPaths.Root, "tests/licensing/integration/Fixtures", $"p06-11-ascii85-{stream}.pdf"));

    private static byte[] MissingAscii85End()
    {
        var bytes = Ascii85Fixture("xref");
        var end = bytes.AsSpan().LastIndexOf("~>"u8);
        Assert.True(end >= 0);
        bytes[end + 1] = (byte)'!';
        return bytes;
    }

    private static byte[] ClassicOnePage(string kids) => Classic([
        "<< /Type /Catalog /Pages 2 0 R >>", Pages(kids, 1),
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 160] >>"]);

    // Two sibling branches. The root count stays correct even when the first branch's count is wrong.
    private static byte[] NestedPages(string firstCount) => Classic([
        "<< /Type /Catalog /Pages 2 0 R >>", Pages("[3 0 R 4 0 R]", 2),
        $"<< /Type /Pages /Parent 2 0 R /Kids [5 0 R] /Count {firstCount} >>",
        "<< /Type /Pages /Parent 2 0 R /Kids [6 0 R] /Count 1 >>", Page,
        "<< /Type /Page /Parent 4 0 R /MediaBox [0 0 240 160] >>", "1"]);

    private static string Pages(string kids, int count) =>
        string.Create(CultureInfo.InvariantCulture, $"<< /Type /Pages /Kids {kids} /Count {count} >>");

    // Catalog, a Pages node and forty leaf pages; `first` is the object number of the first leaf.
    private static string[] FortyPages(string catalog, int first = 4, int? lastKid = null)
    {
        var parent = first - 1;
        var numbers = Enumerable.Range(first, 40).ToArray();
        if (lastKid is { } replaced) numbers[^1] = replaced;
        var kids = "[" + string.Join(' ', numbers.Select(number => $"{number} 0 R")) + "]";
        var page = Page.Replace("/Parent 3 0 R", $"/Parent {parent} 0 R", StringComparison.Ordinal);
        return [catalog, Pages(kids, 40), .. Enumerable.Repeat(page, 40)];
    }

    // Object 1 is a FlateDecode object stream holding `members` as objects 2, 3, ...; the last object
    // is an unfiltered cross-reference stream. `directoryShift` misnumbers the member directory.
    private static byte[] Compressed(string[] members, int root = 2, int directoryShift = 0)
    {
        var directory = new StringBuilder();
        var bodies = new StringBuilder();
        for (var index = 0; index < members.Length; index++)
        {
            directory.Append(CultureInfo.InvariantCulture, $"{index + 2 + directoryShift} {bodies.Length} ");
            bodies.Append(members[index]).Append('\n');
        }
        var packed = Deflate(Encoding.ASCII.GetBytes(directory.ToString() + bodies));
        var xrefNumber = members.Length + 2;
        var head = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture,
            $"%PDF-1.7\n1 0 obj\n<< /Type /ObjStm /N {members.Length} /First {directory.Length} /Filter /FlateDecode /Length {packed.Length} >>\nstream\n"));
        byte[] body = [.. head, .. packed, .. "\nendstream\nendobj\n"u8];

        var entries = new byte[(xrefNumber + 1) * 7];
        BinaryPrimitives.WriteUInt16BigEndian(entries.AsSpan(5), ushort.MaxValue);
        WriteEntry(entries, 1, 1, 9, 0);
        for (var index = 0; index < members.Length; index++) WriteEntry(entries, index + 2, 2, 1, index);
        WriteEntry(entries, xrefNumber, 1, body.Length, 0);
        var xref = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture,
            $"{xrefNumber} 0 obj\n<< /Type /XRef /Root {root} 0 R /Size {xrefNumber + 1} /W [1 4 2] /Length {entries.Length} >>\nstream\n"));
        var tail = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture,
            $"\nendstream\nendobj\nstartxref\n{body.Length}\n%%EOF\n"));
        return [.. body, .. xref, .. entries, .. tail];
    }

    private static void WriteEntry(byte[] entries, int number, byte type, int second, int third)
    {
        var entry = entries.AsSpan(number * 7, 7);
        entry[0] = type;
        BinaryPrimitives.WriteInt32BigEndian(entry[1..], second);
        BinaryPrimitives.WriteUInt16BigEndian(entry[5..], (ushort)third);
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(data);
        return output.ToArray();
    }

    private static byte[] Classic(string[] bodies)
    {
        var pdf = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = [];
        foreach (var body in bodies)
        {
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture, $"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }
        var start = pdf.Length;
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {bodies.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {bodies.Length + 1} /Root 1 0 R >>\nstartxref\n{start}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
