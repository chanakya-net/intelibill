using System.Net;
using System.Text;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0611Tests
{
    [Theory]
    [InlineData("ascii85-object")]
    [InlineData("ascii85-xref")]
    [InlineData("nested-indirect-count")]
    [InlineData("compressed-spine")]
    [InlineData("compressed-indirect-kids")]
    [InlineData("compressed-forty-pages")]
    [InlineData("classic-forty-pages")]
    [InlineData("ascii-hex-xref-name")]
    [InlineData("ascii-hex-xref-array")]
    public async Task ValidPdfRepresentationIsAcceptedAndReplayedExactly(string scenario)
    {
        await using var fixture = await StartAsync(database);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        using var billing = await SignInBillingAsync(fixture);
        var path = Path(purchase.Quote);
        var bytes = P0611PdfFixtures.Valid(scenario);
        var metadata = Metadata(bytes);
        var command = Command(metadata, Digest(await PreviewAsync(billing, path, metadata)));

        using var created = await UploadAsync(billing, path, command, bytes);
        var receipt = await ReadAsync(created, HttpStatusCode.Created);
        Assert.Equal(Hash(bytes), receipt.GetProperty("sha256").GetString());
        Assert.Equal(bytes, await StoredBytesAsync(fixture));
        Assert.Equal(Hash(bytes), await ScalarAsync(fixture, "SELECT content_sha256 FROM billing_document_versions"));

        using var replay = await UploadAsync(billing, path, command, bytes);
        var replayed = await ReadAsync(replay, HttpStatusCode.OK);
        Assert.Equal(receipt.GetRawText(), replayed.GetRawText());
        Assert.Equal(1L, await fixture.CountAsync("billing_document_versions"));
    }

    [Theory]
    [InlineData("ascii85-xref-bad-end", "ascii85-xref")]
    [InlineData("classic-no-reachable-page", "classic-one-page")]
    [InlineData("compressed-no-reachable-page", "compressed-spine")]
    [InlineData("indirect-kids-no-reachable-page", "compressed-indirect-kids")]
    [InlineData("nested-count-mismatch", "nested-indirect-count")]
    [InlineData("compressed-root-wrong-type", "compressed-spine")]
    [InlineData("compressed-pages-wrong-type", "compressed-spine")]
    [InlineData("compressed-member-identity", "compressed-spine")]
    [InlineData("compressed-member-unparsable", "compressed-spine")]
    [InlineData("compressed-kid-wrong-type", "compressed-spine")]
    [InlineData("compressed-indirect-kids-wrong-type", "compressed-indirect-kids")]
    [InlineData("compressed-late-kid-wrong-type", "compressed-forty-pages")]
    [InlineData("classic-late-kid-missing", "classic-forty-pages")]
    [InlineData("classic-late-kid-wrong-type", "classic-forty-pages")]
    [InlineData("ascii-hex-xref-malformed", "ascii-hex-xref-name")]
    [InlineData("ascii-hex-xref-short", "ascii-hex-xref-name")]
    public async Task DamagedPdfObjectsAreRefusedAndOriginalValidPdfStillAttaches(string scenario, string original)
    {
        await using var fixture = await StartAsync(database);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        using var billing = await SignInBillingAsync(fixture);
        var path = Path(purchase.Quote);
        var damaged = P0611PdfFixtures.Damaged(scenario);
        var valid = P0611PdfFixtures.Valid(original);

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

    internal static byte[] AsciiHexCrossReferenceStreamPdf(string filter, Func<string, string>? damage = null)
    {
        var raw = CrossReferenceStreamPdf();
        var marker = Encoding.ASCII.GetBytes("/Length 49 >>\nstream\n");
        var start = raw.AsSpan().IndexOf(marker);
        Assert.True(start > 0);
        var entries = raw.AsSpan(start + marker.Length, 49);
        var hex = Convert.ToHexString(entries) + ">";
        hex = damage?.Invoke(hex) ?? hex;
        var head = Encoding.ASCII.GetString(raw.AsSpan(0, start));
        var tail = Encoding.ASCII.GetString(raw.AsSpan(start + marker.Length + 49));
        return Encoding.ASCII.GetBytes($"{head}/Filter {filter} /Length {hex.Length} >>\nstream\n{hex}{tail}");
    }
}
