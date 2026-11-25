using System.Buffers.Binary;
using System.Globalization;
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
}
