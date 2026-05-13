using System.Xml.Linq;
using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Infrastructure.Services.Exports;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Exports;

public sealed class SalesTallyXmlExportRendererTests
{
    [Fact]
    public async Task RenderAsync_WithValidDataset_ReturnsValidXmlEnvelope()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 1000m, 100m, 900m, 0m, 900m, 162m, 1062m, null, 0m, 0m, 0m, 1062m, false, 2)
        };

        var lineItems = new List<SalesExportLineItemRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Apple", 10m, 50m, 0m, 0m, 18m, 500m, 90m, 590m, false, 0m, null, null)
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 900m, 162m, 0m, 0m)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, lineItems, taxBreakup, []);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);

        Assert.Equal("application/xml", result.ContentType);
        Assert.NotEmpty(result.Content);

        var xmlString = System.Text.Encoding.UTF8.GetString(result.Content);
        var doc = XDocument.Parse(xmlString);
        var root = doc.Root;

        Assert.NotNull(root);
        Assert.Equal("ENVELOPE", root.Name.LocalName);
    }

    [Fact]
    public async Task RenderAsync_WithValidDataset_CreatesValidLedgerMasters()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Customer A", "Cash", 1000m, 100m, 900m, 0m, 900m, 162m, 1062m, null, 0m, 0m, 0m, 1062m, false, 2)
        };

        var lineItems = new List<SalesExportLineItemRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Customer A", "Product", 10m, 50m, 0m, 0m, 18m, 500m, 90m, 590m, false, 0m, null, null)
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 900m, 162m, 0m, 0m)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, lineItems, taxBreakup, []);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var xmlString = System.Text.Encoding.UTF8.GetString(result.Content);
        var doc = XDocument.Parse(xmlString);
        var root = doc.Root;

        Assert.NotNull(root);
        var ledgerMasters = root.Descendants("MASTER")
            .Where(m => m.Attribute("NAME")?.Value == "LEDGER")
            .ToList();

        Assert.NotNull(ledgerMasters);
        Assert.NotEmpty(ledgerMasters);

        var ledgerNames = ledgerMasters
            .Select(m => m.Element("NAME")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();
        Assert.Contains("Sales", ledgerNames);
        Assert.Contains("Cash", ledgerNames);
        Assert.Contains("Customer A", ledgerNames);
        Assert.Contains("Output GST 18%", ledgerNames);
    }

    [Fact]
    public async Task RenderAsync_WithValidDataset_CreatesValidSalesVoucher()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 1000m, 100m, 900m, 0m, 900m, 162m, 1062m, null, 0m, 0m, 0m, 1062m, false, 2)
        };

        var lineItems = new List<SalesExportLineItemRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Product", 10m, 50m, 0m, 0m, 18m, 500m, 90m, 590m, false, 0m, null, null)
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 900m, 162m, 0m, 0m)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, lineItems, taxBreakup, []);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var xmlString = System.Text.Encoding.UTF8.GetString(result.Content);
        var doc = XDocument.Parse(xmlString);
        var root = doc.Root;
        Assert.NotNull(root);

        var vouchers = root.Descendants("VOUCHER").ToList();
        Assert.NotNull(vouchers);
        Assert.Single(vouchers);

        var voucher = vouchers[0];
        var voucherNumber = voucher.Element("VOUCHERNUMBER")?.Value;
        var voucherType = voucher.Element("VOUCHERTYPE")?.Value;

        Assert.Equal("INV-001", voucherNumber);
        Assert.Equal("Sales", voucherType);
    }

    [Fact]
    public async Task RenderAsync_WithValidDataset_GroupsGstByTaxRate()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Customer A", "Cash", 900m, 100m, 800m, 0m, 800m, 144m, 944m, null, 0m, 0m, 0m, 944m, false, 2)
        };

        var lineItems = new List<SalesExportLineItemRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Customer A", "Product 1", 10m, 40m, 0m, 0m, 18m, 400m, 72m, 472m, false, 0m, null, null),
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Customer A", "Product 2", 5m, 80m, 0m, 0m, 5m, 400m, 20m, 420m, false, 0m, null, null)
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 400m, 72m, 0m, 0m),
            new(5m, 400m, 20m, 0m, 0m)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, lineItems, taxBreakup, []);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var xmlString = System.Text.Encoding.UTF8.GetString(result.Content);
        var doc = XDocument.Parse(xmlString);
        var root = doc.Root;
        Assert.NotNull(root);

        var ledgerMasters = root.Descendants("MASTER")
            .Where(m => m.Attribute("NAME")?.Value == "LEDGER")
            .ToList();

        var ledgerNames = ledgerMasters
            .Select(m => m.Element("NAME")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();
        Assert.Contains("Output GST 18%", ledgerNames);
        Assert.Contains("Output GST 5%", ledgerNames);

        var vouchers = root!.Descendants("VOUCHER").ToList();
        Assert.NotNull(vouchers);
        Assert.Single(vouchers);

        var voucherLines = vouchers[0].Elements("VOUCHERLINE").ToList();
        var gstLines = voucherLines
            .Where(line => line.Element("LEDGER")?.Value?.StartsWith("Output GST", StringComparison.Ordinal) ?? false)
            .ToList();

        Assert.True(gstLines.Count >= 2, "Expected at least 2 GST ledger lines for different rates");
    }

    [Fact]
    public async Task RenderAsync_WithEmptyDataset_ReturnsValidEnvelopeWithNoVouchers()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var dataset = new SalesExportDatasetDto(
            metadata,
            new List<SalesExportSummaryRowDto>(),
            new List<SalesExportLineItemRowDto>(),
            new List<SalesExportTaxBreakupDto>(),
            []);

        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);

        Assert.Equal("application/xml", result.ContentType);
        Assert.NotEmpty(result.Content);

        var xmlString = System.Text.Encoding.UTF8.GetString(result.Content);
        var doc = XDocument.Parse(xmlString);
        var root = doc.Root;

        Assert.NotNull(root);
        Assert.Equal("ENVELOPE", root.Name.LocalName);

        var vouchers = root.Descendants("VOUCHER").ToList();
        Assert.Empty(vouchers);

        var header = root.Element("HEADER");
        Assert.NotNull(header);
    }

    [Fact]
    public async Task RenderAsync_WithCustomerName_UsesCustomerNameInVoucher()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "John Doe", "Cash", 1000m, 0m, 900m, 0m, 900m, 162m, 1062m, null, 0m, 0m, 0m, 1062m, false, 2)
        };

        var lineItems = new List<SalesExportLineItemRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "John Doe", "Product", 10m, 50m, 0m, 0m, 18m, 500m, 90m, 590m, false, 0m, null, null)
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 900m, 162m, 0m, 0m)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, lineItems, taxBreakup, []);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var xmlString = System.Text.Encoding.UTF8.GetString(result.Content);
        var doc = XDocument.Parse(xmlString);
        var root = doc.Root;
        Assert.NotNull(root);

        var ledgerMasters = root.Descendants("MASTER")
            .Where(m => m.Attribute("NAME")?.Value == "LEDGER")
            .ToList();

        var ledgerNames = ledgerMasters
            .Select(m => m.Element("NAME")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();
        Assert.Contains("John Doe", ledgerNames);
    }

    [Fact]
    public async Task RenderAsync_WithNullCustomerName_UsesWalkInCustomer()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), null, "Cash", 1000m, 0m, 900m, 0m, 900m, 162m, 1062m, null, 0m, 0m, 0m, 1062m, false, 2)
        };

        var lineItems = new List<SalesExportLineItemRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), null, "Product", 10m, 50m, 0m, 0m, 18m, 500m, 90m, 590m, false, 0m, null, null)
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 900m, 162m, 0m, 0m)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, lineItems, taxBreakup, []);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var xmlString = System.Text.Encoding.UTF8.GetString(result.Content);
        var doc = XDocument.Parse(xmlString);
        var root = doc.Root;
        Assert.NotNull(root);

        var ledgerMasters = root.Descendants("MASTER")
            .Where(m => m.Attribute("NAME")?.Value == "LEDGER")
            .ToList();

        var ledgerNames = ledgerMasters
            .Select(m => m.Element("NAME")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();
        Assert.Contains("Walk-in Customer", ledgerNames);
    }

    [Fact]
    public async Task RenderAsync_WithPaymentMethods_CreatesCorrectPaymentLedgers()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Customer A", "UPI", 1000m, 0m, 900m, 0m, 900m, 162m, 1062m, null, 0m, 0m, 0m, 1062m, false, 2),
            new("INV-002", new DateTimeOffset(2026, 5, 2, 11, 0, 0, TimeSpan.Zero), "Customer B", "Card", 800m, 0m, 900m, 0m, 900m, 162m, 1062m, null, 0m, 0m, 0m, 1062m, false, 2)
        };

        var lineItems = new List<SalesExportLineItemRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Customer A", "Product", 10m, 50m, 0m, 0m, 18m, 500m, 90m, 590m, false, 0m, null, null),
            new("INV-002", new DateTimeOffset(2026, 5, 2, 11, 0, 0, TimeSpan.Zero), "Customer B", "Product", 10m, 50m, 0m, 0m, 18m, 500m, 90m, 590m, false, 0m, null, null)
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 1800m, 324m, 0m, 0m)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, lineItems, taxBreakup, []);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var xmlString = System.Text.Encoding.UTF8.GetString(result.Content);
        var doc = XDocument.Parse(xmlString);
        var root = doc.Root;
        Assert.NotNull(root);

        var ledgerMasters = root.Descendants("MASTER")
            .Where(m => m.Attribute("NAME")?.Value == "LEDGER")
            .ToList();

        var ledgerNames = ledgerMasters
            .Select(m => m.Element("NAME")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();
        Assert.Contains("UPI", ledgerNames);
        Assert.Contains("Card", ledgerNames);
    }

    [Fact]
    public async Task RenderAsync_WithMultipleSales_CreatesMultipleVouchers()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 1000m, 0m, 900m, 0m, 900m, 162m, 1062m, null, 0m, 0m, 0m, 1062m, false, 2),
            new("INV-002", new DateTimeOffset(2026, 5, 2, 11, 0, 0, TimeSpan.Zero), "Bob", "UPI", 800m, 0m, 900m, 0m, 900m, 162m, 1062m, null, 0m, 0m, 0m, 1062m, false, 2)
        };

        var lineItems = new List<SalesExportLineItemRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Product", 10m, 50m, 0m, 0m, 18m, 500m, 90m, 590m, false, 0m, null, null),
            new("INV-002", new DateTimeOffset(2026, 5, 2, 11, 0, 0, TimeSpan.Zero), "Bob", "Product", 10m, 50m, 0m, 0m, 18m, 500m, 90m, 590m, false, 0m, null, null)
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 1800m, 324m, 0m, 0m)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, lineItems, taxBreakup, []);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var xmlString = System.Text.Encoding.UTF8.GetString(result.Content);
        var doc = XDocument.Parse(xmlString);
        var root = doc.Root;

        var vouchers = root!.Descendants("VOUCHER").ToList();
        Assert.NotNull(vouchers);
        Assert.Equal(2, vouchers.Count);

        var voucherNumbers = vouchers.Select(v => v.Element("VOUCHERNUMBER")?.Value).ToList();
        Assert.Contains("INV-001", voucherNumbers);
        Assert.Contains("INV-002", voucherNumbers);
    }

    [Fact]
    public async Task RenderAsync_WithReturns_CalculatesGrossTaxInSalesVoucherCorrectly()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 590m, 0m, 500m, 0m, 500m, 90m, 590m, "R-001", 295m, 250m, 45m, 295m, true, 1)
        };

        var lineItems = new List<SalesExportLineItemRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Product", 10m, 50m, 0m, 0m, 18m, 500m, 90m, 590m, false, 5m, "Returned", "R-001")
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 500m, 90m, 250m, 45m)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, lineItems, taxBreakup, []);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var xmlString = System.Text.Encoding.UTF8.GetString(result.Content);
        var doc = XDocument.Parse(xmlString);
        var root = doc.Root;
        Assert.NotNull(root);

        var vouchers = root.Descendants("VOUCHER").ToList();
        Assert.NotNull(vouchers);
        var salesVoucher = vouchers.Single(v => v.Element("VOUCHERTYPE")?.Value == "Sales");

        var voucherLines = salesVoucher.Elements("VOUCHERLINE").ToList();
        var gstLine = voucherLines.FirstOrDefault(line => 
            (line.Element("LEDGER")?.Value ?? "").StartsWith("Output GST", StringComparison.Ordinal));

        Assert.NotNull(gstLine);
        var grossTax = taxBreakup[0].SaleTaxAmount;
        var gstAmount = decimal.Parse(
            gstLine.Element("AMOUNT")?.Value ?? "0",
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(grossTax, gstAmount);
    }

    [Fact]
    public async Task RenderAsync_WithMultipleSales_GroupsGstLinesPerInvoice()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 118m, 0m, 100m, 0m, 100m, 18m, 118m, null, 0m, 0m, 0m, 118m, false, 1),
            new("INV-002", new DateTimeOffset(2026, 5, 2, 11, 0, 0, TimeSpan.Zero), "Bob", "Cash", 210m, 0m, 200m, 0m, 200m, 10m, 210m, null, 0m, 0m, 0m, 210m, false, 1)
        };

        var lineItems = new List<SalesExportLineItemRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Product A", 1m, 100m, 0m, 0m, 18m, 100m, 18m, 118m, false, 0m, null, null),
            new("INV-002", new DateTimeOffset(2026, 5, 2, 11, 0, 0, TimeSpan.Zero), "Bob", "Product B", 2m, 100m, 0m, 0m, 5m, 200m, 10m, 210m, false, 0m, null, null)
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(5m, 200m, 10m, 0m, 0m),
            new(18m, 100m, 18m, 0m, 0m)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, lineItems, taxBreakup, []);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var xmlString = System.Text.Encoding.UTF8.GetString(result.Content);
        var doc = XDocument.Parse(xmlString);
        var root = doc.Root;
        Assert.NotNull(root);

        var vouchers = root.Descendants("VOUCHER").ToList();
        Assert.NotNull(vouchers);
        Assert.Equal(2, vouchers.Count);

        var voucherByNumber = vouchers.ToDictionary(v => v.Element("VOUCHERNUMBER")!.Value);

        var firstGstLine = voucherByNumber["INV-001"]?.Elements("VOUCHERLINE")
            .Single(line => line.Element("LEDGER")?.Value == "Output GST 18%");
        var secondGstLine = voucherByNumber["INV-002"]?.Elements("VOUCHERLINE")
            .Single(line => line.Element("LEDGER")?.Value == "Output GST 5%");

        var firstGstAmount = decimal.Parse(
            firstGstLine?.Element("AMOUNT")?.Value ?? "0",
            System.Globalization.CultureInfo.InvariantCulture);
        var secondGstAmount = decimal.Parse(
            secondGstLine?.Element("AMOUNT")?.Value ?? "0",
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(18m, firstGstAmount);
        Assert.Equal(10m, secondGstAmount);
    }

    [Fact]
    public async Task RenderAsync_WithDueAmount_CreatesDebtorLine()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 800m, 262m, 900m, 0m, 900m, 162m, 1062m, null, 0m, 0m, 0m, 1062m, false, 2)
        };

        var lineItems = new List<SalesExportLineItemRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Product", 10m, 50m, 0m, 0m, 18m, 500m, 90m, 590m, false, 0m, null, null)
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 900m, 162m, 0m, 0m)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, lineItems, taxBreakup, []);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var xmlString = System.Text.Encoding.UTF8.GetString(result.Content);
        var doc = XDocument.Parse(xmlString);
        var root = doc.Root;
        Assert.NotNull(root);

        var vouchers = root.Descendants("VOUCHER").ToList();
        Assert.NotNull(vouchers);
        var voucherLines = vouchers[0].Elements("VOUCHERLINE").ToList();
        var debtorLine = voucherLines.FirstOrDefault(line => 
            line.Element("LEDGER")?.Value == "Alice" && 
            line.Element("ISDEBIT")?.Value == "Yes");

        Assert.NotNull(debtorLine);
        Assert.Equal("262.00", debtorLine.Element("AMOUNT")?.Value);
    }

    [Fact]
    public async Task RenderAsync_WithSummaryLevelMultipleSales_CreatesGstLinesPerVoucher()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 118m, 0m, 100m, 0m, 100m, 18m, 118m, null, 0m, 0m, 0m, 118m, false, 1),
            new("INV-002", new DateTimeOffset(2026, 5, 2, 11, 0, 0, TimeSpan.Zero), "Bob", "UPI", 236m, 0m, 200m, 0m, 200m, 36m, 236m, null, 0m, 0m, 0m, 236m, false, 1)
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 300m, 54m, 0m, 0m)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, [], taxBreakup, []);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(result.Content));

        var vouchers = doc.Root!.Descendants("VOUCHER")
            .ToDictionary(v => v.Element("VOUCHERNUMBER")!.Value);

        var firstGstLine = vouchers["INV-001"].Elements("VOUCHERLINE")
            .Single(line => line.Element("LEDGER")?.Value == "Output GST 18%");
        var secondGstLine = vouchers["INV-002"].Elements("VOUCHERLINE")
            .Single(line => line.Element("LEDGER")?.Value == "Output GST 18%");

        Assert.Equal("18.00", firstGstLine.Element("AMOUNT")?.Value);
        Assert.Equal("36.00", secondGstLine.Element("AMOUNT")?.Value);
        Assert.Equal("No", firstGstLine.Element("ISDEBIT")?.Value);
        Assert.Equal("No", secondGstLine.Element("ISDEBIT")?.Value);
    }

    [Fact]
    public async Task RenderAsync_WithSummaryLevelMultipleSalesAndMultipleRates_DoesNotAllocateDatasetTaxRates()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 118m, 0m, 100m, 0m, 100m, 18m, 118m, null, 0m, 0m, 0m, 118m, false, 1),
            new("INV-002", new DateTimeOffset(2026, 5, 2, 11, 0, 0, TimeSpan.Zero), "Bob", "UPI", 105m, 0m, 100m, 0m, 100m, 5m, 105m, null, 0m, 0m, 0m, 105m, false, 1)
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(5m, 100m, 5m, 0m, 0m),
            new(18m, 100m, 18m, 0m, 0m)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, [], taxBreakup, []);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(result.Content));

        var voucherGstLines = doc.Root!.Descendants("VOUCHER")
            .SelectMany(v => v.Elements("VOUCHERLINE"))
            .Where(line => line.Element("LEDGER")?.Value.StartsWith("Output GST", StringComparison.Ordinal) == true)
            .ToList();

        Assert.Empty(voucherGstLines);
    }

    [Fact]
    public async Task RenderAsync_WithNormalSale_UsesTaxableSalesCreditAndDebitPayment()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 118m, 0m, 100m, 0m, 100m, 18m, 118m, null, 0m, 0m, 0m, 118m, false, 1)
        };

        var lineItems = new List<SalesExportLineItemRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Product", 1m, 100m, 0m, 0m, 18m, 100m, 18m, 118m, false, 0m, null, null)
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 100m, 18m, 0m, 0m)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, lineItems, taxBreakup, []);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(result.Content));
        var voucherLines = doc.Root!.Descendants("VOUCHER").Single().Elements("VOUCHERLINE").ToList();

        var salesLine = voucherLines.Single(line => line.Element("LEDGER")?.Value == "Sales");
        var paymentLine = voucherLines.Single(line => line.Element("LEDGER")?.Value == "Cash");

        Assert.Equal("100.00", salesLine.Element("AMOUNT")?.Value);
        Assert.Equal("No", salesLine.Element("ISDEBIT")?.Value);
        Assert.Equal("118.00", paymentLine.Element("AMOUNT")?.Value);
        Assert.Equal("Yes", paymentLine.Element("ISDEBIT")?.Value);
    }

    [Fact]
    public async Task RenderAsync_WithReturns_CreatesCreditNoteVouchers()
    {
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Alice", "Cash", 118m, 0m, 100m, 0m, 100m, 18m, 118m, "R-001", 118m, 100m, 18m, 0m, true, 1)
        };

        var returnTaxBreakup = new List<SalesExportReturnTaxBreakupDto>
        {
            new(18m, 100m, 18m)
        };

        var returnRows = new List<SalesExportReturnRowDto>
        {
            new("R-001", new DateTimeOffset(2026, 5, 2, 10, 0, 0, TimeSpan.Zero), "INV-001", "Alice", 118m, 100m, 18m, returnTaxBreakup)
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 100m, 18m, 100m, 18m)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, [], taxBreakup, returnRows);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(result.Content));
        
        var vouchers = doc.Root!.Descendants("VOUCHER").ToList();
        var creditNote = vouchers.Single(v => v.Element("VOUCHERTYPE")?.Value == "Credit Note");

        Assert.Equal("R-001", creditNote.Element("VOUCHERNUMBER")?.Value);
        
        var voucherLines = creditNote.Elements("VOUCHERLINE").ToList();
        
        var customerLine = voucherLines.Single(l => l.Element("LEDGER")?.Value == "Alice");
        Assert.Equal("118.00", customerLine.Element("AMOUNT")?.Value);
        Assert.Equal("No", customerLine.Element("ISDEBIT")?.Value);

        var salesLine = voucherLines.Single(l => l.Element("LEDGER")?.Value == "Sales");
        Assert.Equal("100.00", salesLine.Element("AMOUNT")?.Value);
        Assert.Equal("Yes", salesLine.Element("ISDEBIT")?.Value);

        var gstLine = voucherLines.Single(l => l.Element("LEDGER")?.Value == "Output GST 18%");
        Assert.Equal("18.00", gstLine.Element("AMOUNT")?.Value);
        Assert.Equal("Yes", gstLine.Element("ISDEBIT")?.Value);
    }

    [Fact]
    public async Task RenderAsync_WithReturnRows_GeneratesCreditNoteVouchers()
    {
        var metadata = new SalesExportMetadataDto(
            "Test Shop",
            "123 Test St",
            "27ABCDE1234F1Z5",
            "Test User",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Customer A", "Cash", 1000m, 0m, 1000m, 0m, 1000m, 180m, 1180m, "RET-001", 200m, 100m, 18m, 980m, true, 1)
        };

        var returnRows = new List<SalesExportReturnRowDto>
        {
            new("RET-001", new DateTimeOffset(2026, 5, 5, 11, 0, 0, TimeSpan.Zero), "INV-001", "Customer A", 200m, 100m, 18m, new List<SalesExportReturnTaxBreakupDto> { new(18m, 100m, 18m) })
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 1000m, 180m, 100m, 18m)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, [], taxBreakup, returnRows);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(result.Content));
        var vouchers = doc.Root!.Descendants("VOUCHER").ToList();

        var creditNoteVoucher = vouchers.SingleOrDefault(v => v.Element("VOUCHERTYPE")?.Value == "Credit Note");
        Assert.NotNull(creditNoteVoucher);
        Assert.Equal("RET-001", creditNoteVoucher.Element("VOUCHERNUMBER")?.Value);
    }

    [Fact]
    public async Task RenderAsync_CreditNoteVoucher_ContainsCustomerLedgerLine()
    {
        var metadata = new SalesExportMetadataDto(
            "Test Shop",
            "123 Test St",
            "27ABCDE1234F1Z5",
            "Test User",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var returnRows = new List<SalesExportReturnRowDto>
        {
            new("RET-001", new DateTimeOffset(2026, 5, 5, 11, 0, 0, TimeSpan.Zero), "INV-001", "Customer A", 200m, 100m, 18m, new List<SalesExportReturnTaxBreakupDto> { new(18m, 100m, 18m) })
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 0m, 0m, 100m, 18m)
        };

        var dataset = new SalesExportDatasetDto(metadata, [], [], taxBreakup, returnRows);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(result.Content));
        var creditNoteVoucher = doc.Root!.Descendants("VOUCHER").Single(v => v.Element("VOUCHERTYPE")?.Value == "Credit Note");
        var voucherLines = creditNoteVoucher.Elements("VOUCHERLINE").ToList();

        var customerLine = voucherLines.Single(line => line.Element("LEDGER")?.Value == "Customer A");
        Assert.Equal("200.00", customerLine.Element("AMOUNT")?.Value);
        Assert.Equal("No", customerLine.Element("ISDEBIT")?.Value);
    }

    [Fact]
    public async Task RenderAsync_CreditNoteVoucher_ContainsSalesReversalLine()
    {
        var metadata = new SalesExportMetadataDto(
            "Test Shop",
            "123 Test St",
            "27ABCDE1234F1Z5",
            "Test User",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var returnRows = new List<SalesExportReturnRowDto>
        {
            new("RET-001", new DateTimeOffset(2026, 5, 5, 11, 0, 0, TimeSpan.Zero), "INV-001", "Customer A", 200m, 100m, 18m, new List<SalesExportReturnTaxBreakupDto> { new(18m, 100m, 18m) })
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 0m, 0m, 100m, 18m)
        };

        var dataset = new SalesExportDatasetDto(metadata, [], [], taxBreakup, returnRows);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(result.Content));
        var creditNoteVoucher = doc.Root!.Descendants("VOUCHER").Single(v => v.Element("VOUCHERTYPE")?.Value == "Credit Note");
        var voucherLines = creditNoteVoucher.Elements("VOUCHERLINE").ToList();

        var salesLine = voucherLines.Single(line => line.Element("LEDGER")?.Value == "Sales");
        Assert.Equal("100.00", salesLine.Element("AMOUNT")?.Value);
        Assert.Equal("Yes", salesLine.Element("ISDEBIT")?.Value);
    }

    [Fact]
    public async Task RenderAsync_CreditNoteVoucher_ContainsGstReversalLines()
    {
        var metadata = new SalesExportMetadataDto(
            "Test Shop",
            "123 Test St",
            "27ABCDE1234F1Z5",
            "Test User",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var returnRows = new List<SalesExportReturnRowDto>
        {
            new("RET-001", new DateTimeOffset(2026, 5, 5, 11, 0, 0, TimeSpan.Zero), "INV-001", "Customer A", 200m, 100m, 18m, new List<SalesExportReturnTaxBreakupDto> { new(18m, 100m, 18m) })
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 0m, 0m, 100m, 18m)
        };

        var dataset = new SalesExportDatasetDto(metadata, [], [], taxBreakup, returnRows);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(result.Content));
        var creditNoteVoucher = doc.Root!.Descendants("VOUCHER").Single(v => v.Element("VOUCHERTYPE")?.Value == "Credit Note");
        var voucherLines = creditNoteVoucher.Elements("VOUCHERLINE").ToList();

        var gstLine = voucherLines.Single(line => line.Element("LEDGER")?.Value == "Output GST 18%");
        Assert.Equal("18.00", gstLine.Element("AMOUNT")?.Value);
        Assert.Equal("Yes", gstLine.Element("ISDEBIT")?.Value);
    }

    [Fact]
    public async Task RenderAsync_WithMultipleReturns_GeneratesMultipleCreditNotes()
    {
        var metadata = new SalesExportMetadataDto(
            "Test Shop",
            "123 Test St",
            "27ABCDE1234F1Z5",
            "Test User",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var returnRows = new List<SalesExportReturnRowDto>
        {
            new("RET-001", new DateTimeOffset(2026, 5, 5, 11, 0, 0, TimeSpan.Zero), "INV-001", "Customer A", 100m, 50m, 9m, new List<SalesExportReturnTaxBreakupDto> { new(18m, 50m, 9m) }),
            new("RET-002", new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero), "INV-002", "Customer B", 150m, 75m, 13.50m, new List<SalesExportReturnTaxBreakupDto> { new(18m, 75m, 13.50m) })
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 0m, 0m, 125m, 22.5m)
        };

        var dataset = new SalesExportDatasetDto(metadata, [], [], taxBreakup, returnRows);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(result.Content));
        var creditNoteVouchers = doc.Root!.Descendants("VOUCHER").Where(v => v.Element("VOUCHERTYPE")?.Value == "Credit Note").ToList();

        Assert.Equal(2, creditNoteVouchers.Count);
        Assert.Contains(creditNoteVouchers, v => v.Element("VOUCHERNUMBER")?.Value == "RET-001");
        Assert.Contains(creditNoteVouchers, v => v.Element("VOUCHERNUMBER")?.Value == "RET-002");
    }

    [Fact]
    public async Task RenderAsync_WithReturnRows_GeneratesCustomerLedgerMaster()
    {
        var metadata = new SalesExportMetadataDto(
            "Test Shop",
            "123 Test St",
            "27ABCDE1234F1Z5",
            "Test User",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var returnRows = new List<SalesExportReturnRowDto>
        {
            new("RET-001", new DateTimeOffset(2026, 5, 5, 11, 0, 0, TimeSpan.Zero), "INV-001", "Customer A", 200m, 100m, 18m, new List<SalesExportReturnTaxBreakupDto> { new(18m, 100m, 18m) })
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 0m, 0m, 100m, 18m)
        };

        var dataset = new SalesExportDatasetDto(metadata, [], [], taxBreakup, returnRows);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(result.Content));
        var ledgerMasters = doc.Root!.Descendants("MASTER")
            .Where(m => m.Attribute("NAME")?.Value == "LEDGER")
            .ToList();
        var ledgerNames = ledgerMasters
            .Select(m => m.Element("NAME")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();

        Assert.Contains("Customer A", ledgerNames);
    }

    [Fact]
    public async Task RenderAsync_WithVoidedReturnRows_DoesNotCreateCreditNoteVouchers()
    {
        var metadata = new SalesExportMetadataDto(
            "Test Shop",
            "123 Test St",
            "27ABCDE1234F1Z5",
            "Test User",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Customer A", "Cash", 118m, 0m, 100m, 0m, 100m, 18m, 118m, null, 0m, 0m, 0m, 118m, false, 1)
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 100m, 18m, 100m, 18m)
        };

        var returnRows = new List<SalesExportReturnRowDto>
        {
            new("RET-001", new DateTimeOffset(2026, 5, 5, 11, 0, 0, TimeSpan.Zero), "INV-001", "Customer A", 118m, 100m, 18m, new List<SalesExportReturnTaxBreakupDto> { new(18m, 100m, 18m) }, true)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, [], taxBreakup, returnRows);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(result.Content));
        var vouchers = doc.Root!.Descendants("VOUCHER").ToList();

        Assert.Single(vouchers);
        Assert.DoesNotContain(vouchers, v => v.Element("VOUCHERTYPE")?.Value == "Credit Note");
    }

    [Fact]
    public async Task RenderAsync_CreditNoteReferencesToSaleInvoice()
    {
        var metadata = new SalesExportMetadataDto(
            "Test Shop",
            "123 Test St",
            "27ABCDE1234F1Z5",
            "Test User",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var returnRows = new List<SalesExportReturnRowDto>
        {
            new("RET-001", new DateTimeOffset(2026, 5, 5, 11, 0, 0, TimeSpan.Zero), "INV-001", "Customer A", 200m, 100m, 18m, new List<SalesExportReturnTaxBreakupDto> { new(18m, 100m, 18m) })
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 0m, 0m, 100m, 18m)
        };

        var dataset = new SalesExportDatasetDto(metadata, [], [], taxBreakup, returnRows);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(result.Content));
        var creditNoteVoucher = doc.Root!.Descendants("VOUCHER").Single(v => v.Element("VOUCHERTYPE")?.Value == "Credit Note");

        var reference = creditNoteVoucher.Element("REFERENCE");
        Assert.NotNull(reference);
        Assert.Equal("INV-001", reference.Element("REFERENCENUMBER")?.Value);
    }

    [Fact]
    public async Task RenderAsync_WithSalesAndReturns_GeneratesBothVoucherTypes()
    {
        var metadata = new SalesExportMetadataDto(
            "Test Shop",
            "123 Test St",
            "27ABCDE1234F1Z5",
            "Test User",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "summary");

        var summaryRows = new List<SalesExportSummaryRowDto>
        {
            new("INV-001", new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), "Customer A", "Cash", 1000m, 0m, 1000m, 0m, 1000m, 180m, 1180m, "RET-001", 200m, 100m, 18m, 980m, true, 1)
        };

        var returnRows = new List<SalesExportReturnRowDto>
        {
            new("RET-001", new DateTimeOffset(2026, 5, 5, 11, 0, 0, TimeSpan.Zero), "INV-001", "Customer A", 200m, 100m, 18m, new List<SalesExportReturnTaxBreakupDto> { new(18m, 100m, 18m) })
        };

        var taxBreakup = new List<SalesExportTaxBreakupDto>
        {
            new(18m, 1000m, 180m, 100m, 18m)
        };

        var dataset = new SalesExportDatasetDto(metadata, summaryRows, [], taxBreakup, returnRows);
        var renderer = new SalesTallyXmlExportRenderer();

        var result = await renderer.RenderAsync(dataset, CancellationToken.None);
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(result.Content));
        var vouchers = doc.Root!.Descendants("VOUCHER").ToList();

        var salesVouchers = vouchers.Where(v => v.Element("VOUCHERTYPE")?.Value == "Sales").ToList();
        var creditNoteVouchers = vouchers.Where(v => v.Element("VOUCHERTYPE")?.Value == "Credit Note").ToList();

        Assert.Single(salesVouchers);
        Assert.Single(creditNoteVouchers);

        // Reconciliation: verify numeric values
        var salesVoucher = salesVouchers[0];
        var creditNoteVoucher = creditNoteVouchers[0];

        // Extract ledger amounts from vouchers
        var salesVoucherLines = salesVoucher.Descendants("VOUCHERLINE").ToList();
        var creditNoteLines = creditNoteVoucher.Descendants("VOUCHERLINE").ToList();

        // Sales voucher should have: Sales line (1000), Customer line (1000), Payment line (1000), GST line (180)
        var salesLines = salesVoucherLines.Where(l => l.Element("LEDGER")?.Value == "Sales").ToList();
        var gstSalesLines = salesVoucherLines.Where(l => l.Element("LEDGER")?.Value?.StartsWith("Output GST", StringComparison.Ordinal) == true).ToList();

        // Credit note should have: Customer line (200), Sales line (100), GST line (18)
        var creditNoteSalesLines = creditNoteLines.Where(l => l.Element("LEDGER")?.Value == "Sales").ToList();
        var gstCreditNoteLines = creditNoteLines.Where(l => l.Element("LEDGER")?.Value?.StartsWith("Output GST", StringComparison.Ordinal) == true).ToList();

        Assert.Single(salesLines);
        Assert.NotEmpty(gstSalesLines);
        Assert.Single(creditNoteSalesLines);
        Assert.NotEmpty(gstCreditNoteLines);

        // Verify the sales line in credit note is debit (reversal)
        var creditNoteSalesLine = creditNoteSalesLines[0];
        Assert.Equal("Yes", creditNoteSalesLine.Element("ISDEBIT")?.Value);
        Assert.Equal("100.00", creditNoteSalesLine.Element("AMOUNT")?.Value);
    }

}
