using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Text;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0611Tests
{
    [Fact]
    public async Task CrossReferenceStreamPdfIsAcceptedWithoutReencoding()
    {
        await using var fixture = await StartAsync(database);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        using var billing = await SignInBillingAsync(fixture);
        var bytes = CrossReferenceStreamPdf();
        var saved = await AttachAsync(billing, Path(purchase.Quote), bytes);
        Assert.Equal(Hash(bytes), saved.GetProperty("sha256").GetString());
        Assert.Equal(bytes, await StoredBytesAsync(fixture));
    }

    [Theory]
    [InlineData("p06-11-linearized-stream-compact.pdf")]
    [InlineData("p06-11-linearized-table-compact.pdf")]
    public async Task LinearizedCompactPdfFixtureIsAcceptedAndReplayedExactly(string fixtureFile)
    {
        await using var fixture = await StartAsync(database);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        using var billing = await SignInBillingAsync(fixture);
        var path = Path(purchase.Quote);
        var bytes = await File.ReadAllBytesAsync(System.IO.Path.Combine(
            ProjectPaths.Root, "tests/licensing/integration/Fixtures", fixtureFile));
        var metadata = Metadata(bytes);
        var command = Command(metadata, Digest(await PreviewAsync(billing, path, metadata)));

        using var created = await UploadAsync(billing, path, command, bytes);
        var receipt = await ReadAsync(created, HttpStatusCode.Created);
        Assert.Equal(Hash(bytes), receipt.GetProperty("sha256").GetString());
        Assert.Equal(bytes.Length, receipt.GetProperty("byteLength").GetInt32());
        Assert.Equal(bytes, await StoredBytesAsync(fixture));
        Assert.Equal(Hash(bytes), await ScalarAsync(fixture, "SELECT content_sha256 FROM billing_document_versions"));

        using var replay = await UploadAsync(billing, path, command, bytes);
        var replayed = await ReadAsync(replay, HttpStatusCode.OK);
        Assert.Equal(receipt.GetRawText(), replayed.GetRawText());
        Assert.Equal(1L, await fixture.CountAsync("billing_document_versions"));
    }

    [Fact]
    public async Task IncrementalCompactPdfIsAcceptedWithoutReencoding()
    {
        await using var fixture = await StartAsync(database);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        using var billing = await SignInBillingAsync(fixture);
        var bytes = IncrementalClassicPdf();
        var saved = await AttachAsync(billing, Path(purchase.Quote), bytes);
        Assert.Equal(Hash(bytes), saved.GetProperty("sha256").GetString());
        Assert.Equal(bytes, await StoredBytesAsync(fixture));
    }

    [Theory]
    [InlineData("stale-stream-length")]
    [InlineData("truncated-stream-data")]
    [InlineData("corrupt-flate-stream")]
    [InlineData("broken-root-reference")]
    [InlineData("non-catalog-root")]
    [InlineData("broken-prev-reference")]
    public async Task DamagedPdfStructureIsRefusedAndOriginalValidPdfStillAttaches(string scenario)
    {
        await using var fixture = await StartAsync(database);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        using var billing = await SignInBillingAsync(fixture);
        var path = Path(purchase.Quote);
        var (damaged, valid) = scenario switch
        {
            "stale-stream-length" => (DamagedStreamWithStaleLength(truncateData: false), CrossReferenceStreamPdf()),
            "truncated-stream-data" => (DamagedStreamWithStaleLength(truncateData: true), CrossReferenceStreamPdf()),
            "corrupt-flate-stream" => DamagedLinearizedFlateStream(),
            "broken-root-reference" => (ReplaceLatin1(Pdf(), "/Root 1 0 R", "/Root 99 0 R"), Pdf()),
            "non-catalog-root" => (ReplaceLatin1(Pdf(), "/Root 1 0 R", "/Root 4 0 R"), Pdf()),
            "broken-prev-reference" => (ReplaceLatin1(Pdf(), "/Root 1 0 R", "/Prev 99999 /Root 1 0 R"), Pdf()),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        var damagedMeta = Metadata(damaged);
        var damagedCommand = Command(damagedMeta, Digest(await PreviewAsync(billing, path, damagedMeta)));
        using var refused = await UploadAsync(billing, path, damagedCommand, damaged);
        await AssertProblemAsync(refused, HttpStatusCode.BadRequest, "BillingDocument.ContentInvalid");
        Assert.Equal(0L, await fixture.CountAsync("billing_document_versions"));

        var saved = await AttachAsync(billing, path, valid);
        Assert.Equal(Hash(valid), saved.GetProperty("sha256").GetString());
        Assert.Equal(1L, await fixture.CountAsync("billing_document_versions"));
        Assert.Equal(valid, await StoredBytesAsync(fixture));
    }

    private static byte[] CrossReferenceStreamPdf()
    {
        var classic = Encoding.ASCII.GetString(Pdf());
        var pointer = classic.IndexOf("\nxref\n", StringComparison.Ordinal) + 1;
        var entries = new byte[7 * 7];
        BinaryPrimitives.WriteUInt16BigEndian(entries.AsSpan(5), ushort.MaxValue);
        for (var number = 1; number <= 6; number++)
        {
            var offset = number == 6 ? pointer : classic.IndexOf(
                string.Create(CultureInfo.InvariantCulture, $"\n{number} 0 obj\n"), StringComparison.Ordinal) + 1;
            var entry = entries.AsSpan(number * 7, 7);
            entry[0] = 1;
            BinaryPrimitives.WriteInt32BigEndian(entry[1..], offset);
        }
        var header = classic[..pointer] + "6 0 obj\n<< /Type /XRef /Root 1 0 R /Size 7 /W [1 4 2] /Length 49 >>\nstream\n";
        var footer = string.Create(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{pointer}\n%%EOF\n");
        return [.. Encoding.ASCII.GetBytes(header), .. entries, .. Encoding.ASCII.GetBytes(footer)];
    }

    private static byte[] DamagedStreamWithStaleLength(bool truncateData)
    {
        var valid = CrossReferenceStreamPdf();
        if (!truncateData) return ReplaceLatin1(valid, "/Length 49", "/Length 35");
        var marker = "\nendstream\n"u8;
        var end = valid.AsSpan().LastIndexOf(marker);
        Assert.True(end > 12);
        return [.. valid[..(end - 12)], .. valid[end..]];
    }

    private static byte[] IncrementalClassicPdf()
    {
        var baseText = Encoding.ASCII.GetString(Pdf());
        var prevOffset = baseText.IndexOf("\nxref\n", StringComparison.Ordinal) + 1;
        var update = new StringBuilder(baseText);
        var infoOffset = update.Length;
        update.Append("6 0 obj\n<</Producer(Fixture Incremental)>>\nendobj\n");
        var xrefOffset = update.Length;
        update.Append(CultureInfo.InvariantCulture, $"xref\n6 1\n{infoOffset:D10} 00000 n \n");
        update.Append(CultureInfo.InvariantCulture,
            $"trailer\n<</Size 7/Prev {prevOffset}/Info 6 0 R>>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(update.ToString());
    }

    private static (byte[] Damaged, byte[] Valid) DamagedLinearizedFlateStream()
    {
        var valid = File.ReadAllBytes(System.IO.Path.Combine(
            ProjectPaths.Root, "tests/licensing/integration/Fixtures/p06-11-linearized-stream-compact.pdf"));
        var damaged = valid.ToArray();
        var marker = ">>stream\r\n"u8;
        var streamStart = damaged.AsSpan().IndexOf(marker) + marker.Length;
        Assert.True(streamStart > marker.Length);
        damaged[streamStart + 4] ^= 0xFF;
        return (damaged, valid);
    }

    private static byte[] ReplaceLatin1(byte[] source, string oldValue, string newValue)
    {
        var text = Encoding.Latin1.GetString(source);
        Assert.Contains(oldValue, text, StringComparison.Ordinal);
        return Encoding.Latin1.GetBytes(text.Replace(oldValue, newValue, StringComparison.Ordinal));
    }
}
