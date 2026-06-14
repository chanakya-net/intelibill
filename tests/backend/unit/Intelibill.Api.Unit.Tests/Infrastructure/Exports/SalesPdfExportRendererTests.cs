using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Infrastructure.Services.Exports;
using System.Text;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Exports;

public sealed class SalesPdfExportRendererTests
{
    [Fact]
    public async Task RenderAsync_WithSummaryDataset_ReturnsNonEmptyPdfBytes()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            SalesExportLevel.Summary);

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 1200m, 0m, 1200m, 0m, 1000m, 180m, 1180m, null, 0m, 0m, 0m, 1180m, false, 2)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, [], [], []);
        var renderer = new SalesPdfExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);

        Assert.Equal("application/pdf", result.ContentType);
        Assert.NotEmpty(result.Content);
        Assert.True(result.Content.AsSpan(0, Math.Min(result.Content.Length, 5)).SequenceEqual("%PDF-"u8));
    }

    [Fact]
    public async Task RenderAsync_WithLineItemDataset_ReturnsNonEmptyPdfBytes()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            SalesExportLevel.LineItems);

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 1200m, 0m, 1200m, 0m, 1000m, 180m, 1180m, null, 0m, 0m, 0m, 1180m, false, 2)
        };

        var lineItemRows = new List<SalesExportLineItemRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Apple", 2m, 50m, 0m, 0m, 18m, 100m, 18m, 118m, false, 0m, null, null)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, lineItemRows, [], []);
        var renderer = new SalesPdfExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);

        Assert.Equal("application/pdf", result.ContentType);
        Assert.NotEmpty(result.Content);
        Assert.True(result.Content.AsSpan(0, Math.Min(result.Content.Length, 5)).SequenceEqual("%PDF-"u8));
    }

    [Fact]
    public async Task RenderAsync_WithCreditNoteFields_IncludesCreditNoteMetricsInSummary()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            SalesExportLevel.Summary);

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 200m, 50m, 250m, 0m, 250m, 50m, 300m, null, 0m, 0m, 0m, 300m, false, 1, 50m, "CN-001", 500m)
        };

        var baselineRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 200m, 50m, 250m, 0m, 250m, 50m, 300m, null, 0m, 0m, 0m, 300m, false, 1)
        };

        var creditDataset = new SalesExportDatasetDto(metadata, summaryRows, [], [], []);
        var baselineDataset = new SalesExportDatasetDto(metadata, baselineRows, [], [], []);
        var renderer = new SalesPdfExportRenderer();

        var creditResult = await renderer.RenderAsync(creditDataset, CancellationToken.None);
        var baselineResult = await renderer.RenderAsync(baselineDataset, CancellationToken.None);

        Assert.Equal("application/pdf", creditResult.ContentType);
        Assert.NotEmpty(creditResult.Content);
        Assert.Equal("application/pdf", baselineResult.ContentType);
        Assert.NotEmpty(baselineResult.Content);
        Assert.NotEqual(baselineResult.Content.Length, creditResult.Content.Length);
        Assert.NotEqual(
            GetStreamChecksumsAsHexString(baselineResult.Content),
            GetStreamChecksumsAsHexString(creditResult.Content));
    }

    private static string GetStreamChecksumsAsHexString(byte[] pdfContent)
    {
        var checksums = ExtractPdfStreams(pdfContent)
            .Select(stream => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)))
            .ToArray();

        return string.Join(";", checksums);
    }

    private static IEnumerable<byte[]> ExtractPdfStreams(byte[] pdfContent)
    {
        var streamKeyword = Encoding.UTF8.GetBytes("stream");
        var endStreamKeyword = Encoding.UTF8.GetBytes("endstream");
        var cursor = 0;

        while (TryFindBytePattern(pdfContent, streamKeyword, cursor, out var streamStart))
        {
            var contextStart = Math.Max(0, streamStart - 512);
            var context = Encoding.UTF8.GetString(pdfContent[contextStart..streamStart]);
            var headerHasFlate = context.Contains("/Filter", StringComparison.OrdinalIgnoreCase)
                && context.Contains("FlateDecode", StringComparison.OrdinalIgnoreCase);

            var dataStart = streamStart + streamKeyword.Length;
            if (dataStart >= pdfContent.Length)
            {
                yield break;
            }

            if (pdfContent[dataStart] == 0x0D && dataStart + 1 < pdfContent.Length && pdfContent[dataStart + 1] == 0x0A)
            {
                dataStart += 2;
            }
            else if (pdfContent[dataStart] == 0x0A)
            {
                dataStart++;
            }

            var dataEnd = dataStart;
            if (!TryFindBytePattern(pdfContent, endStreamKeyword, dataStart, out var endStreamStart))
            {
                yield break;
            }

            var lengthMatch = Regex.Match(context, @"/Length\s+(\d+)");
            if (lengthMatch.Success && int.TryParse(lengthMatch.Groups[1].Value, out var length))
            {
                dataEnd = dataStart + length;
                if (dataEnd > pdfContent.Length || length < 0)
                {
                    dataEnd = endStreamStart;
                }
            }
            else
            {
                dataEnd = endStreamStart;
            }

            while (dataEnd > dataStart && (pdfContent[dataEnd - 1] == 0x0D || pdfContent[dataEnd - 1] == 0x0A))
            {
                dataEnd--;
            }

            var streamData = pdfContent[dataStart..dataEnd];
            yield return streamData;

            if (headerHasFlate)
            {
                var inflated = InflateStream(streamData);
                if (inflated is not null && inflated.Length > 0)
                {
                    yield return inflated;
                }
            }

            cursor = Math.Max(dataEnd + 1, endStreamStart + endStreamKeyword.Length);
        }
    }

    private static bool TryFindBytePattern(byte[] source, byte[] pattern, int startIndex, out int index)
    {
        for (var i = startIndex; i <= source.Length - pattern.Length; i++)
        {
            var isMatch = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (source[i + j] != pattern[j])
                {
                    isMatch = false;
                    break;
                }
            }

            if (isMatch)
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static byte[]? InflateStream(byte[] stream)
    {
        try
        {
            using var input = new MemoryStream(stream);
            using var output = new MemoryStream();
            using (var decompression = new DeflateStream(input, CompressionMode.Decompress))
            {
                decompression.CopyTo(output);
            }

            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }

}
