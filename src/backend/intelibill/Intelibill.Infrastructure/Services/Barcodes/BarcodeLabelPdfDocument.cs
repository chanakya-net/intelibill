using System.Globalization;
using Intelibill.Application.Features.Items.Barcodes;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.SkiaSharp;
using ZXing.SkiaSharp.Rendering;

namespace Intelibill.Infrastructure.Services.Barcodes;

internal sealed class BarcodeLabelPdfDocument(BarcodeLabelPrintDataset dataset) : IDocument
{
    private const float Millimeter = 72f / 25.4f;
    private const float LabelWidth = 50f * Millimeter;
    private const float LabelHeight = 30f * Millimeter;

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(LabelWidth, LabelHeight);
            page.Margin(4);
            page.DefaultTextStyle(style => style.FontSize(6));

            page.Content().Column(column =>
            {
                for (var index = 0; index < dataset.Rows.Count; index++)
                {
                    var row = dataset.Rows[index];
                    column.Item().Element(item => ComposeLabel(item, row));
                    if (index < dataset.Rows.Count - 1)
                    {
                        column.Item().PageBreak();
                    }
                }
            });
        });
    }

    private static void ComposeLabel(IContainer container, BarcodeLabelPrintRow row)
    {
        var barcodeImage = GenerateBarcodeImage(row.Barcode);

        container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten1)
            .Padding(2)
            .Column(column =>
            {
                column.Spacing(1);
                column.Item().Text(row.ItemName).SemiBold().FontSize(6);
                column.Item().AlignCenter().Height(22).Image(barcodeImage).FitArea();
                column.Item().AlignCenter().Text(row.Barcode).FontSize(5);
                column.Item().AlignCenter().Text(row.ShopName).FontSize(5);
                column.Item().Row(prices =>
                {
                    prices.RelativeItem().Text(row.Mrp.HasValue
                        ? $"MRP: {row.Mrp.Value.ToString("N2", CultureInfo.InvariantCulture)}"
                        : string.Empty);
                    prices.RelativeItem().AlignRight().Text(row.SalesPrice.HasValue
                        ? $"SP: {row.SalesPrice.Value.ToString("N2", CultureInfo.InvariantCulture)}"
                        : string.Empty);
                });
            });
    }

    private static byte[] GenerateBarcodeImage(string barcodeValue)
    {
        var writer = new BarcodeWriter<SKBitmap>
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions
            {
                Width = 360,
                Height = 120,
                Margin = 0,
                PureBarcode = true,
            },
            Renderer = new SKBitmapRenderer(),
        };

        using var bitmap = writer.Write(barcodeValue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
