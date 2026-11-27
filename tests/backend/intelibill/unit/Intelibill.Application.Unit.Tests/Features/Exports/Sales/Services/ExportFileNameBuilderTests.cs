using Intelibill.Application.Features.Exports.Sales.Services;
using Intelibill.Infrastructure.Services.Exports;

namespace Intelibill.Application.Unit.Tests.Features.Exports.Sales.Services;

public class ExportFileNameBuilderTests
{
    private readonly ExportFileNameBuilder _builder = new();

    [Fact]
    public void BuildFileName_WithNormalShopName_GeneratesValidFilename()
    {
        var shopName = "My Shop";
        var format = "xlsx";
        var level = "summary";
        var startDate = new DateOnly(2025, 1, 1);
        var endDate = new DateOnly(2025, 1, 31);

        var result = _builder.BuildFileName(shopName, format, level, startDate, endDate);

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.EndsWith(".xlsx", result);
        Assert.Contains("my-shop", result);
        Assert.Contains("sales", result);
        Assert.Contains("summary", result);
        Assert.Contains("2025-01-01", result);
        Assert.Contains("2025-01-31", result);
    }

    [Fact]
    public void BuildFileName_WithPdfFormat_GeneratesValidPdfFilename()
    {
        var shopName = "Test Shop";
        var format = "pdf";
        var level = "lineItems";
        var startDate = new DateOnly(2025, 2, 1);
        var endDate = new DateOnly(2025, 2, 28);

        var result = _builder.BuildFileName(shopName, format, level, startDate, endDate);

        Assert.EndsWith(".pdf", result);
        Assert.Contains("application/pdf", _builder.GetContentType(format));
    }

    [Fact]
    public void BuildFileName_WithTallyFormat_GeneratesValidTallyFilename()
    {
        var shopName = "Shop Name";
        var format = "tallyXml";
        string? level = null;
        var startDate = new DateOnly(2025, 3, 1);
        var endDate = new DateOnly(2025, 3, 31);

        var result = _builder.BuildFileName(shopName, format, level, startDate, endDate);

        Assert.EndsWith(".xml", result);
        Assert.Contains("sales-tally", result);
    }

    [Fact]
    public void BuildFileName_WithShopNameContainingSpaces_ConvertedToHyphens()
    {
        var shopName = "My Test Shop";
        var format = "xlsx";
        var level = "summary";
        var startDate = new DateOnly(2025, 1, 1);
        var endDate = new DateOnly(2025, 1, 31);

        var result = _builder.BuildFileName(shopName, format, level, startDate, endDate);

        Assert.Contains("my-test-shop", result);
    }

    [Fact]
    public void BuildFileName_WithShopNameContainingPunctuation_RemovesUnsafeCharacters()
    {
        var shopName = "Shop's & Co.";
        var format = "xlsx";
        var level = "summary";
        var startDate = new DateOnly(2025, 1, 1);
        var endDate = new DateOnly(2025, 1, 31);

        var result = _builder.BuildFileName(shopName, format, level, startDate, endDate);

        Assert.DoesNotContain("'", result);
        Assert.DoesNotContain("&", result);
        Assert.DoesNotContain(".", result.Split('-')[0]); // shop name part shouldn't end with period
        Assert.NotEmpty(result);
    }

    [Fact]
    public void BuildFileName_WithEmptyShopName_UsesDefaultFallback()
    {
        var shopName = "";
        var format = "xlsx";
        var level = "summary";
        var startDate = new DateOnly(2025, 1, 1);
        var endDate = new DateOnly(2025, 1, 31);

        var result = _builder.BuildFileName(shopName, format, level, startDate, endDate);

        Assert.Contains("shop", result.ToLowerInvariant());
    }

    [Fact]
    public void BuildFileName_WithRepeatedHyphens_CollapsesDuplicates()
    {
        var shopName = "Shop---Test";
        var format = "xlsx";
        var level = "summary";
        var startDate = new DateOnly(2025, 1, 1);
        var endDate = new DateOnly(2025, 1, 31);

        var result = _builder.BuildFileName(shopName, format, level, startDate, endDate);

        Assert.DoesNotContain("--", result);
    }

    [Fact]
    public void GetContentType_WithXlsx_ReturnsExcelContentType()
    {
        var contentType = _builder.GetContentType("xlsx");

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", contentType);
    }

    [Fact]
    public void GetContentType_WithPdf_ReturnsPdfContentType()
    {
        var contentType = _builder.GetContentType("pdf");

        Assert.Equal("application/pdf", contentType);
    }

    [Fact]
    public void GetContentType_WithTallyXml_ReturnsXmlContentType()
    {
        var contentType = _builder.GetContentType("tallyXml");

        Assert.Equal("application/xml", contentType);
    }
}
