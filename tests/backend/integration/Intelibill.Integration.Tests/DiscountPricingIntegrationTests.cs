using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class DiscountPricingIntegrationTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory _factory = new(fixture);

    public async Task InitializeAsync() => await _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task RecordSale_AppliesConfiguredBatchPercentageDiscount_SnapshotsFields()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inbound = await AddInventoryAsync(
            client,
            ownerToken,
            barcode,
            batchNumber: "B-BATCH-DISC-001",
            quantity: 5m,
            costPrice: 80m,
            salesPrice: 100m,
            taxRatePercent: 0m,
            taxIncluded: false);
        var batchId = inbound.GetProperty("inventoryBatchId").GetGuid();

        var ruleId = await CreateDiscountRuleAsync(client, ownerToken, new
        {
            ruleType = "BatchPercentage",
            name = "10% off batch",
            description = (string?)null,
            percentage = 10m,
            thresholdAmount = (decimal?)null,
            inventoryBatchId = batchId,
            startsAt = (DateTimeOffset?)null,
            endsAt = (DateTimeOffset?)null,
            belowCostConfirmed = false,
            belowCostConfirmationReason = (string?)null,
        });

        var preview = await PreviewSaleAsync(client, ownerToken, new
        {
            saleDiscount = new { type = (int)InstantDiscountType.None, value = 0m },
            items = new[]
            {
                new
                {
                    inventoryBatchId = batchId,
                    barcode,
                    batchNumber = "B-BATCH-DISC-001",
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 0m,
                    isPriceIncludingTax = false,
                    itemDiscount = new { type = (int)InstantDiscountType.None, value = 0m },
                    clientLineKey = (string?)null,
                },
            },
        });
        Assert.Equal(90m, preview.GetProperty("totalAmount").GetDecimal());

        var saleId = await RecordSaleAsync(client, ownerToken, new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Configured Batch Discount",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = preview.GetProperty("totalAmount").GetDecimal(),
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-BATCH-DISC-001",
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 0m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).SingleAsync(s => s.Id == saleId);
        var item = Assert.Single(sale.Items);

        Assert.Equal(100m, sale.TotalBeforeDiscount);
        Assert.Equal(10m, sale.TotalDiscountAmount);
        Assert.Equal(90m, sale.TotalAmount);

        Assert.Equal(100m, item.PreTaxAmountBeforeDiscount);
        Assert.Equal(10m, item.ItemDiscountAmount);
        Assert.Equal(0m, item.SaleDiscountAmount);
        Assert.Equal(90m, item.TaxableAmount);
        Assert.Equal(90m, item.TotalAmount);

        Assert.Equal(InstantDiscountType.None, item.ItemDiscountOverrideType);
        Assert.Equal(0m, item.ItemDiscountOverrideValue);
        Assert.Equal(ruleId, item.ConfiguredBatchRuleId);
        Assert.Equal(10m, item.ConfiguredBatchRulePercentage);
    }

    [Fact]
    public async Task RecordSale_AppliesConfiguredSalePercentageDiscount_SnapshotsFields()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inbound = await AddInventoryAsync(
            client,
            ownerToken,
            barcode,
            batchNumber: "B-SALE-DISC-001",
            quantity: 5m,
            costPrice: 80m,
            salesPrice: 100m,
            taxRatePercent: 0m,
            taxIncluded: false);
        var batchId = inbound.GetProperty("inventoryBatchId").GetGuid();

        var ruleId = await CreateDiscountRuleAsync(client, ownerToken, new
        {
            ruleType = "SalePercentage",
            name = "10% off sale",
            description = (string?)null,
            percentage = 10m,
            thresholdAmount = (decimal?)null,
            inventoryBatchId = (Guid?)null,
            startsAt = (DateTimeOffset?)null,
            endsAt = (DateTimeOffset?)null,
            belowCostConfirmed = false,
            belowCostConfirmationReason = (string?)null,
        });

        var preview = await PreviewSaleAsync(client, ownerToken, new
        {
            saleDiscount = new { type = (int)InstantDiscountType.None, value = 0m },
            items = new[]
            {
                new
                {
                    inventoryBatchId = batchId,
                    barcode,
                    batchNumber = "B-SALE-DISC-001",
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 0m,
                    isPriceIncludingTax = false,
                    itemDiscount = new { type = (int)InstantDiscountType.None, value = 0m },
                    clientLineKey = (string?)null,
                },
            },
        });
        Assert.Equal(90m, preview.GetProperty("totalAmount").GetDecimal());
        Assert.NotEqual(JsonValueKind.Null, preview.GetProperty("configuredSaleRule").ValueKind);

        var saleId = await RecordSaleAsync(client, ownerToken, new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Configured Sale Discount",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = preview.GetProperty("totalAmount").GetDecimal(),
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-SALE-DISC-001",
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 0m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).SingleAsync(s => s.Id == saleId);
        var item = Assert.Single(sale.Items);

        Assert.Equal(100m, sale.TotalBeforeDiscount);
        Assert.Equal(10m, sale.TotalDiscountAmount);
        Assert.Equal(90m, sale.TotalAmount);

        Assert.Equal(ruleId, sale.ConfiguredSaleRuleId);
        Assert.Equal(DiscountRuleType.SalePercentage, sale.ConfiguredSaleRuleType);
        Assert.Equal(InstantDiscountType.None, sale.SaleDiscountOverrideType);
        Assert.Equal(0m, sale.SaleDiscountOverrideValue);

        Assert.Equal(0m, item.ItemDiscountAmount);
        Assert.Equal(10m, item.SaleDiscountAmount);
        Assert.Null(item.ConfiguredBatchRuleId);
        Assert.Null(item.ConfiguredBatchRulePercentage);
    }

    [Fact]
    public async Task PreviewAndRecordSale_CapsInstantSaleDiscountOverride_AtConfiguredPercentage()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inbound = await AddInventoryAsync(
            client,
            ownerToken,
            barcode,
            batchNumber: "B-CAP-001",
            quantity: 5m,
            costPrice: 80m,
            salesPrice: 100m,
            taxRatePercent: 0m,
            taxIncluded: false);
        var batchId = inbound.GetProperty("inventoryBatchId").GetGuid();

        var ruleId = await CreateDiscountRuleAsync(client, ownerToken, new
        {
            ruleType = "SalePercentage",
            name = "10% configured",
            description = (string?)null,
            percentage = 10m,
            thresholdAmount = (decimal?)null,
            inventoryBatchId = (Guid?)null,
            startsAt = (DateTimeOffset?)null,
            endsAt = (DateTimeOffset?)null,
            belowCostConfirmed = false,
            belowCostConfirmationReason = (string?)null,
        });

        var preview = await PreviewSaleAsync(client, ownerToken, new
        {
            saleDiscount = new { type = (int)InstantDiscountType.Percentage, value = 50m },
            items = new[]
            {
                new
                {
                    inventoryBatchId = batchId,
                    barcode,
                    batchNumber = "B-CAP-001",
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 0m,
                    isPriceIncludingTax = false,
                    itemDiscount = new { type = (int)InstantDiscountType.None, value = 0m },
                    clientLineKey = (string?)null,
                },
            },
        });

        Assert.Equal(10m, preview.GetProperty("totalDiscountAmount").GetDecimal());
        Assert.Equal(90m, preview.GetProperty("totalAmount").GetDecimal());

        var saleId = await RecordSaleAsync(client, ownerToken, new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Capped override",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = preview.GetProperty("totalAmount").GetDecimal(),
            dueAmount = 0m,
            saleDiscount = new { type = (int)InstantDiscountType.Percentage, value = 50m },
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-CAP-001",
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 0m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).SingleAsync(s => s.Id == saleId);
        var item = Assert.Single(sale.Items);

        Assert.Equal(ruleId, sale.ConfiguredSaleRuleId);
        Assert.Equal(DiscountRuleType.SalePercentage, sale.ConfiguredSaleRuleType);
        Assert.Equal(InstantDiscountType.Percentage, sale.SaleDiscountOverrideType);
        Assert.Equal(50m, sale.SaleDiscountOverrideValue);

        Assert.Equal(10m, sale.TotalDiscountAmount);
        Assert.Equal(10m, item.SaleDiscountAmount);
    }

    [Fact]
    public async Task RecordSale_AppliesConfiguredSaleThresholdPercentageDiscount_SnapshotsFields()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inbound = await AddInventoryAsync(
            client,
            ownerToken,
            barcode,
            batchNumber: "B-THRESH-001",
            quantity: 5m,
            costPrice: 80m,
            salesPrice: 100m,
            taxRatePercent: 0m,
            taxIncluded: false);
        var batchId = inbound.GetProperty("inventoryBatchId").GetGuid();

        var ruleId = await CreateDiscountRuleAsync(client, ownerToken, new
        {
            ruleType = "SaleThresholdPercentage",
            name = "10% over 150",
            description = (string?)null,
            percentage = 10m,
            thresholdAmount = 150m,
            inventoryBatchId = (Guid?)null,
            startsAt = (DateTimeOffset?)null,
            endsAt = (DateTimeOffset?)null,
            belowCostConfirmed = false,
            belowCostConfirmationReason = (string?)null,
        });

        var preview = await PreviewSaleAsync(client, ownerToken, new
        {
            saleDiscount = new { type = (int)InstantDiscountType.None, value = 0m },
            items = new[]
            {
                new
                {
                    inventoryBatchId = batchId,
                    barcode,
                    batchNumber = "B-THRESH-001",
                    itemName = "Test Item",
                    quantity = 2m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 0m,
                    isPriceIncludingTax = false,
                    itemDiscount = new { type = (int)InstantDiscountType.None, value = 0m },
                    clientLineKey = (string?)null,
                },
            },
        });

        Assert.Equal(20m, preview.GetProperty("totalDiscountAmount").GetDecimal());
        Assert.Equal(180m, preview.GetProperty("totalTaxableAmount").GetDecimal());
        Assert.Equal(180m, preview.GetProperty("totalAmount").GetDecimal());

        var line = preview.GetProperty("lines").EnumerateArray().Single();
        Assert.Equal(200m, line.GetProperty("preTaxAmountBeforeDiscount").GetDecimal());

        var previewRule = preview.GetProperty("configuredSaleRule");
        Assert.Equal(ruleId, previewRule.GetProperty("ruleId").GetGuid());
        Assert.Equal("SaleThresholdPercentage", previewRule.GetProperty("ruleType").GetString());
        Assert.Equal(10m, previewRule.GetProperty("percentage").GetDecimal());
        Assert.Equal(150m, previewRule.GetProperty("thresholdAmount").GetDecimal());

        var saleId = await RecordSaleAsync(client, ownerToken, new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Threshold Discount",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = preview.GetProperty("totalAmount").GetDecimal(),
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-THRESH-001",
                    itemName = "Test Item",
                    quantity = 2m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 0m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).SingleAsync(s => s.Id == saleId);
        var item = Assert.Single(sale.Items);

        Assert.Equal(200m, sale.TotalBeforeDiscount);
        Assert.Equal(20m, sale.TotalDiscountAmount);
        Assert.Equal(180m, sale.TotalAmount);

        Assert.Equal(ruleId, sale.ConfiguredSaleRuleId);
        Assert.Equal(DiscountRuleType.SaleThresholdPercentage, sale.ConfiguredSaleRuleType);
        Assert.Equal(10m, sale.ConfiguredSaleRulePercentage);
        Assert.Equal(150m, sale.ConfiguredSaleRuleThresholdAmount);

        Assert.Equal(20m, item.SaleDiscountAmount);
    }

    [Fact]
    public async Task PreviewSale_WhenClientPriceMismatch_ReturnsWarning()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inbound = await AddInventoryAsync(
            client,
            ownerToken,
            barcode,
            batchNumber: "B-MISMATCH-001",
            quantity: 5m,
            costPrice: 80m,
            salesPrice: 100m,
            taxRatePercent: 0m,
            taxIncluded: false);
        var batchId = inbound.GetProperty("inventoryBatchId").GetGuid();

        var preview = await PreviewSaleAsync(client, ownerToken, new
        {
            saleDiscount = new { type = (int)InstantDiscountType.None, value = 0m },
            items = new[]
            {
                new
                {
                    inventoryBatchId = batchId,
                    barcode,
                    batchNumber = "B-MISMATCH-001",
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 70m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 0m,
                    isPriceIncludingTax = false,
                    itemDiscount = new { type = (int)InstantDiscountType.None, value = 0m },
                    clientLineKey = "line-1",
                },
            },
        });

        var warnings = preview.GetProperty("warnings").EnumerateArray().ToList();
        Assert.Contains(warnings, w => w.GetProperty("code").GetString() == "sale_preview.warning.client_price_mismatch"
            && w.GetProperty("clientLineKey").GetString() == "line-1");
    }

    [Fact]
    public async Task PreviewSale_WhenConfiguredSaleRuleButNoEligibleLines_ReturnsInfoAndDoesNotApplyRule()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inbound = await AddInventoryAsync(
            client,
            ownerToken,
            barcode,
            batchNumber: "B-NOELIG-001",
            quantity: 5m,
            costPrice: 80m,
            salesPrice: 100m,
            taxRatePercent: 0m,
            taxIncluded: false);
        var batchId = inbound.GetProperty("inventoryBatchId").GetGuid();

        await CreateDiscountRuleAsync(client, ownerToken, new
        {
            ruleType = "SalePercentage",
            name = "10% configured",
            description = (string?)null,
            percentage = 10m,
            thresholdAmount = (decimal?)null,
            inventoryBatchId = (Guid?)null,
            startsAt = (DateTimeOffset?)null,
            endsAt = (DateTimeOffset?)null,
            belowCostConfirmed = false,
            belowCostConfirmationReason = (string?)null,
        });

        var preview = await PreviewSaleAsync(client, ownerToken, new
        {
            saleDiscount = new { type = (int)InstantDiscountType.None, value = 0m },
            items = new[]
            {
                new
                {
                    inventoryBatchId = batchId,
                    barcode,
                    batchNumber = "B-NOELIG-001",
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 0m,
                    isPriceIncludingTax = false,
                    itemDiscount = new { type = (int)InstantDiscountType.Flat, value = 20m },
                    clientLineKey = (string?)null,
                },
            },
        });

        Assert.Equal(JsonValueKind.Null, preview.GetProperty("configuredSaleRule").ValueKind);

        var line = preview.GetProperty("lines").EnumerateArray().Single();
        Assert.Equal(20m, line.GetProperty("itemDiscountAmount").GetDecimal());
        Assert.Equal(0m, line.GetProperty("saleDiscountAmount").GetDecimal());

        var infos = preview.GetProperty("infos").EnumerateArray().ToList();
        Assert.Contains(infos, i => i.GetProperty("code").GetString() == "sale_pricing.info.no_eligible_lines_for_configured_sale_discount");
    }

    [Fact]
    public async Task PreviewSale_WhenSaleDiscountRequestedButNoEligibleLines_ReturnsNoEligibleLinesError()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inbound = await AddInventoryAsync(
            client,
            ownerToken,
            barcode,
            batchNumber: "B-NOELIG-ERR-001",
            quantity: 5m,
            costPrice: 80m,
            salesPrice: 100m,
            taxRatePercent: 0m,
            taxIncluded: false);
        var batchId = inbound.GetProperty("inventoryBatchId").GetGuid();

        var problem = await PreviewSaleExpectingBodyAsync(client, ownerToken, new
        {
            saleDiscount = new { type = (int)InstantDiscountType.Percentage, value = 10m },
            items = new[]
            {
                new
                {
                    inventoryBatchId = batchId,
                    barcode,
                    batchNumber = "B-NOELIG-ERR-001",
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 0m,
                    isPriceIncludingTax = false,
                    itemDiscount = new { type = (int)InstantDiscountType.Flat, value = 20m },
                    clientLineKey = (string?)null,
                },
            },
        }, HttpStatusCode.BadRequest);

        Assert.Equal("SalePricing.NoEligibleLines", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task PreviewSale_WhenInstantItemDiscountWouldBeBelowCost_ReturnsError()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inbound = await AddInventoryAsync(
            client,
            ownerToken,
            barcode,
            batchNumber: "B-BELOWCOST-ITEM-001",
            quantity: 5m,
            costPrice: 80m,
            salesPrice: 100m,
            taxRatePercent: 0m,
            taxIncluded: false);
        var batchId = inbound.GetProperty("inventoryBatchId").GetGuid();

        var problem = await PreviewSaleExpectingBodyAsync(client, ownerToken, new
        {
            saleDiscount = new { type = (int)InstantDiscountType.None, value = 0m },
            items = new[]
            {
                new
                {
                    inventoryBatchId = batchId,
                    barcode,
                    batchNumber = "B-BELOWCOST-ITEM-001",
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 0m,
                    isPriceIncludingTax = false,
                    itemDiscount = new { type = (int)InstantDiscountType.Flat, value = 999m },
                    clientLineKey = (string?)null,
                },
            },
        }, HttpStatusCode.BadRequest);

        Assert.Equal("SalePricing.ItemDiscountBelowCost", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task PreviewSale_WhenSaleDiscountWouldGoBelowCost_ReturnsError()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inbound = await AddInventoryAsync(
            client,
            ownerToken,
            barcode,
            batchNumber: "B-BELOWCOST-SALE-001",
            quantity: 5m,
            costPrice: 80m,
            salesPrice: 100m,
            taxRatePercent: 0m,
            taxIncluded: false);
        var batchId = inbound.GetProperty("inventoryBatchId").GetGuid();

        var problem = await PreviewSaleExpectingBodyAsync(client, ownerToken, new
        {
            saleDiscount = new { type = (int)InstantDiscountType.Flat, value = 30m },
            items = new[]
            {
                new
                {
                    inventoryBatchId = batchId,
                    barcode,
                    batchNumber = "B-BELOWCOST-SALE-001",
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 0m,
                    isPriceIncludingTax = false,
                    itemDiscount = new { type = (int)InstantDiscountType.None, value = 0m },
                    clientLineKey = (string?)null,
                },
            },
        }, HttpStatusCode.BadRequest);

        Assert.Equal("SalePricing.SaleDiscountBelowCost", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task PreviewSale_DoesNotApplyDiscountRuleFromOtherShop()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);

        var shopAToken = await CreateShopAsync(client, token);
        await CreateDiscountRuleAsync(client, shopAToken, new
        {
            ruleType = "SalePercentage",
            name = "10% shop A",
            description = (string?)null,
            percentage = 10m,
            thresholdAmount = (decimal?)null,
            inventoryBatchId = (Guid?)null,
            startsAt = (DateTimeOffset?)null,
            endsAt = (DateTimeOffset?)null,
            belowCostConfirmed = false,
            belowCostConfirmationReason = (string?)null,
        });

        var shopBToken = await CreateShopAsync(client, token);
        var barcode = UniqueBarcode();
        var inbound = await AddInventoryAsync(
            client,
            shopBToken,
            barcode,
            batchNumber: "B-SHOPB-001",
            quantity: 5m,
            costPrice: 80m,
            salesPrice: 100m,
            taxRatePercent: 0m,
            taxIncluded: false);
        var batchId = inbound.GetProperty("inventoryBatchId").GetGuid();

        var preview = await PreviewSaleAsync(client, shopBToken, new
        {
            saleDiscount = new { type = (int)InstantDiscountType.None, value = 0m },
            items = new[]
            {
                new
                {
                    inventoryBatchId = batchId,
                    barcode,
                    batchNumber = "B-SHOPB-001",
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 0m,
                    isPriceIncludingTax = false,
                    itemDiscount = new { type = (int)InstantDiscountType.None, value = 0m },
                    clientLineKey = (string?)null,
                },
            },
        });

        Assert.Equal(JsonValueKind.Null, preview.GetProperty("configuredSaleRule").ValueKind);
        Assert.Equal(0m, preview.GetProperty("totalDiscountAmount").GetDecimal());
        Assert.Equal(100m, preview.GetProperty("totalAmount").GetDecimal());
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static string UniqueEmail() => $"discount-pricing-{Guid.NewGuid():N}@test.com";
    private static string UniqueBarcode() => $"DISC-PRICE-{Guid.NewGuid():N}";

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email = UniqueEmail(),
            password = "Pass123!Aa",
            firstName = "Test",
            lastName = "User",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}"
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static async Task<string> CreateShopAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name = $"Shop {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static async Task<JsonElement> AddInventoryAsync(
        HttpClient client,
        string token,
        string barcode,
        string batchNumber,
        decimal quantity,
        decimal costPrice,
        decimal salesPrice,
        decimal taxRatePercent,
        bool taxIncluded)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            itemName = "Test Item",
            barcode,
            itemDescription = (string?)null,
            uom = "pcs",
            batchNumber,
            quantity,
            totalPurchaseCost = costPrice * quantity,
            mrp = 120m,
            salesPrice,
            taxRatePercent,
            taxIncluded,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId = (Guid?)null,
            referenceNumber = (string?)null,
            notes = (string?)null,
            performedAt = (DateTimeOffset?)null,
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> CreateDiscountRuleAsync(HttpClient client, string token, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/discounts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> PreviewSaleAsync(HttpClient client, string token, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sales/preview");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> PreviewSaleExpectingBodyAsync(
        HttpClient client,
        string token,
        object body,
        HttpStatusCode expectedStatus)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sales/preview");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body);
        var response = await client.SendAsync(request);
        Assert.Equal(expectedStatus, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> RecordSaleAsync(HttpClient client, string token, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saleBody = await response.Content.ReadFromJsonAsync<JsonElement>();
        return saleBody.GetProperty("saleId").GetGuid();
    }
}
