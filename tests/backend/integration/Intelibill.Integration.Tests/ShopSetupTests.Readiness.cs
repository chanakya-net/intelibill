using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Intelibill.Domain.Enums;

namespace Intelibill.Integration.Tests;

public sealed partial class ShopSetupTests
{
    private static readonly string[] TaxComponentKinds = ["Cgst", "Sgst"];

    [Fact]
    public async Task Readiness_TracksSavedCatalogAndAllowsServicesWithoutBankOrStock()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var initial = await GetStatusAsync(client, shop);
        Assert.False(initial.GetProperty("canTrade").GetBoolean());
        Assert.Equal("complete", Step(initial, "identity"));
        Assert.Equal("complete", Step(initial, "tax"));
        Assert.Equal("incomplete", Step(initial, "catalog"));
        Assert.Equal("optional", Step(initial, "bank"));
        Assert.Contains("ShopSetup.CatalogRequired", Reasons(initial));

        _ = await ReadAsync(await SaveAsync(client, shop, 0, true, true));
        Assert.False((await GetStatusAsync(client, shop)).GetProperty("canTrade").GetBoolean());

        var service = await AddServiceAsync(client);
        var serviceOnly = await GetStatusAsync(client, shop);
        Assert.True(serviceOnly.GetProperty("canTrade").GetBoolean());
        Assert.Equal("notApplicable", Step(serviceOnly, "stock"));
        Assert.Equal("optional", Step(serviceOnly, "bank"));

        _ = await ReadAsync(await client.PostAsJsonAsync("/api/items", new
        {
            name = "Empty goods", barcode = "empty", uom = "pcs", isActive = true
        }));
        var mixed = await GetStatusAsync(client, shop);
        Assert.True(mixed.GetProperty("canTrade").GetBoolean());
        Assert.Equal("incomplete", Step(mixed, "stock"));
        Assert.Empty(Reasons(mixed));

        using var deactivate = await client.PostAsync($"/api/services/{service}/deactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        Assert.False((await GetStatusAsync(client, shop)).GetProperty("canTrade").GetBoolean());
    }

    [Fact]
    public async Task Sample_ContinuesPastUnsuitableCatalogAndUsesRealDocumentRules()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        _ = await AddServiceAsync(client, name: "A Tax-bearing service", taxRate: 18);
        var status = await GetStatusAsync(client, shop);
        Assert.False(status.GetProperty("canTrade").GetBoolean());
        Assert.Contains("Invoice.UnregisteredCannotCollectTax", Reasons(status));
        using (var unavailable = await client.GetAsync($"/api/shops/{shop}/setup/sample"))
        {
            Assert.Equal(HttpStatusCode.Conflict, unavailable.StatusCode);
            var body = await unavailable.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("ShopSetup.SampleUnavailable", body.GetProperty("title").GetString());
            Assert.Contains(body.GetProperty("errors").EnumerateArray(),
                error => error.GetProperty("code").GetString() == "Invoice.UnregisteredCannotCollectTax");
        }

        _ = await AddServiceAsync(client, name: "Z Valid service");
        var sample = await ReadAsync(await client.GetAsync($"/api/shops/{shop}/setup/sample"));
        Assert.True(sample.GetProperty("isSample").GetBoolean());
        Assert.Equal("Service", sample.GetProperty("lineType").GetString());
        var document = sample.GetProperty("document");
        Assert.Equal("", document.GetProperty("number").GetString());
        Assert.Equal("Receipt", document.GetProperty("documentType").GetString());
        var line = Assert.Single(document.GetProperty("lines").EnumerateArray());
        Assert.Equal("Z Valid service", line.GetProperty("description").GetString());
        Assert.Equal("NA", line.GetProperty("uom").GetString());
        Assert.Equal(50m, document.GetProperty("totals").GetProperty("invoiceTotal").GetDecimal());
        Assert.True((await GetStatusAsync(client, shop)).GetProperty("canTrade").GetBoolean());
    }

    [Fact]
    public async Task Sample_ScansPastTheFirstPageWithOneDiscountReadPerRequest()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        for (var index = 0; index < 33; index++)
            _ = await AddServiceAsync(client, $"Tax-bearing service {index:D2}", taxRate: 18);

        var viableService = await AddServiceAsync(client, "Viable service");
        var batch = await ReceiveGoodsAsync(client, "viable", "pcs", 2m);
        _ = await ReadAsync(await client.PostAsJsonAsync("/api/discounts", new
        {
            ruleType = "BatchPercentage", name = "Sample batch discount", inventoryBatchId = batch, percentage = 10m
        }));

        var serviceSample = await GetWithDiscountBudgetAsync(client, $"/api/shops/{shop}/setup/sample");
        Assert.Equal("Service", serviceSample.GetProperty("lineType").GetString());
        Assert.Equal("Viable service", serviceSample.GetProperty("document").GetProperty("lines")[0].GetProperty("description").GetString());

        using var deactivate = await client.PostAsync($"/api/services/{viableService}/deactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        var setup = await GetWithDiscountBudgetAsync(client, $"/api/shops/{shop}/setup");
        Assert.True(setup.GetProperty("canTrade").GetBoolean());

        var sample = await GetWithDiscountBudgetAsync(client, $"/api/shops/{shop}/setup/sample");
        Assert.Equal("Goods", sample.GetProperty("lineType").GetString());
        var document = sample.GetProperty("document");
        Assert.Equal("Goods viable", document.GetProperty("lines")[0].GetProperty("description").GetString());
        Assert.Equal(18m, document.GetProperty("totals").GetProperty("invoiceTotal").GetDecimal());
    }

    private async Task<JsonElement> GetWithDiscountBudgetAsync(HttpClient client, string path)
    {
        using var observed = _reads.Observe();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(ReadQueryProbe.Header, observed.Id);
        var result = await ReadAsync(await client.SendAsync(request));
        Assert.Single(observed.Reads, read => read.Sql.Contains("FROM discount_rules ", StringComparison.Ordinal));
        return result;
    }

    [Fact]
    public async Task Sample_FractionalStockAndExistingSequenceAreNotChangedByReads()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var batch = await ReceiveGoodsAsync(client, "fractional", "kg", 0.375m);
        var before = await SnapshotAsync(shop);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var status = await GetStatusAsync(client, shop);
            Assert.True(status.GetProperty("canTrade").GetBoolean());
            Assert.Equal("complete", Step(status, "stock"));
            var sample = await ReadAsync(await client.GetAsync($"/api/shops/{shop}/setup/sample"));
            var document = sample.GetProperty("document");
            Assert.Equal("", document.GetProperty("number").GetString());
            var line = Assert.Single(document.GetProperty("lines").EnumerateArray());
            Assert.Equal("KGS", line.GetProperty("uom").GetString());
            Assert.Equal(0.375m, line.GetProperty("quantity").GetDecimal());
        }
        Assert.Equal(before, await SnapshotAsync(shop));

        _ = await SellGoodsAsync(client, batch, "fractional", 0.25m);
        var afterSale = await SnapshotAsync(shop);
        Assert.NotEqual(before, afterSale);
        _ = await ReadAsync(await client.GetAsync($"/api/shops/{shop}/setup/sample"));
        Assert.Equal(afterSale, await SnapshotAsync(shop));
    }

    [Theory]
    [InlineData("UPDATE items SET is_active = false WHERE shop_id = @shop", "ShopSetup.CatalogRequired")]
    [InlineData("UPDATE inventory_batches SET is_voided = true WHERE shop_id = @shop", "ShopSetup.OpeningStockRequired")]
    [InlineData("UPDATE inventory_batches SET quantity = 0 WHERE shop_id = @shop", "ShopSetup.OpeningStockRequired")]
    [InlineData("DELETE FROM inventory WHERE shop_id = @shop", "Sale.InventoryAggregateNotFound")]
    [InlineData("UPDATE inventory SET quantity = 0 WHERE shop_id = @shop", "ShopSetup.AggregateStockInsufficient")]
    [InlineData("UPDATE inventory_batches SET quantity = 0.125 WHERE shop_id = @shop", "Sale.QuantityPrecisionExceeded")]
    [InlineData("UPDATE items SET unit_review_required = true WHERE shop_id = @shop", "ShopSetup.UnitReviewRequired")]
    public async Task Readiness_RejectsUnsellableGoodsFacts(string change, string code)
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        _ = await ReceiveGoodsAsync(client, "goods", "pcs", 2m);
        Assert.True((await GetStatusAsync(client, shop)).GetProperty("canTrade").GetBoolean());
        await using var connection = new NpgsqlConnection(_factory.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(change, connection);
        command.Parameters.AddWithValue("shop", shop);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        var status = await GetStatusAsync(client, shop);
        Assert.False(status.GetProperty("canTrade").GetBoolean());
        Assert.Contains(code, Reasons(status));
    }

    [Theory]
    [InlineData("Block", false, "Expiry.Expired")]
    [InlineData("Warn", true, "Expiry.ApprovalRequired")]
    public async Task Readiness_RequiresAnUnconditionallySellableLot(string mode, bool overrideAllowed, string reason)
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        _ = await ReceiveGoodsAsync(client, "expired", "pcs", 2m, BusinessDates.Today.AddDays(-1));
        _ = await ReadAsync(await client.PutAsJsonAsync("/api/expiry-policies", new
        {
            expectedCatalogVersion = 1, policy = new { mode, expiryRequired = false, sellThroughExpiryDate = true, warningDays = 7, overrideAllowed },
            reason = "Test setup readiness"
        }));
        Assert.Contains(reason, Reasons(await GetStatusAsync(client, shop)));

        _ = await ReceiveGoodsAsync(client, "fresh", "pcs", 2m, BusinessDates.Today.AddDays(30));
        Assert.True((await GetStatusAsync(client, shop)).GetProperty("canTrade").GetBoolean());
    }

    [Fact]
    public async Task Readiness_RejectsFullyQuarantinedRestockedLot()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var batch = await ReceiveGoodsAsync(client, "expired", "pcs", 2m, BusinessDates.Today.AddDays(-1));
        var sale = await SellGoodsAsync(client, batch, "expired", 2m);
        var saleId = sale.GetProperty("saleId").GetGuid();
        var saleItemId = sale.GetProperty("items")[0].GetProperty("saleItemId").GetGuid();
        _ = await ReadAsync(await client.PostAsJsonAsync($"/api/sales/{saleId}/returns", new
        {
            payoutDestination = (int)ReturnPayoutDestination.Refund,
            items = new[] { new { saleItemId, quantity = 2m, lineType = (int)SaleLineType.Goods, condition = (int)SaleReturnCondition.Restockable } }
        }));
        Assert.Contains("Expiry.Quarantined", Reasons(await GetStatusAsync(client, shop)));
    }

    [Theory]
    [InlineData("Composition", null, 0, null, true, "BillOfSupply")]
    [InlineData("Composition", null, 18, null, false, "Invoice.CompositionCannotCollectTax")]
    [InlineData("Regular", "Required", 18, "998313", false, "Invoice.EInvoiceUnsupported")]
    [InlineData("Regular", "Exempt", 18, "9983", false, "Invoice.HsnRequired")]
    [InlineData("Regular", "Exempt", 18, "998313", true, "TaxInvoice")]
    public async Task Readiness_UsesTheDeclaredProfileAndItsDocumentRules(
        string regime, string? attestation, int taxRate, string? hsn, bool ready, string expected)
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        _ = await ReadAsync(await client.PutAsJsonAsync($"/api/shops/{shop}/tax-profile", new
        {
            taxRegime = regime, stateCode = "29", invoiceSeriesCode = "A", gstNumber = TestTaxProfile.NewGstin(), eInvoiceStatus = attestation
        }));
        _ = await AddServiceAsync(client, taxRate: taxRate, hsn: hsn);
        var status = await GetStatusAsync(client, shop);
        Assert.Equal(ready, status.GetProperty("canTrade").GetBoolean());
        if (!ready)
        {
            Assert.Contains(expected, Reasons(status));
            return;
        }

        var sample = await ReadAsync(await client.GetAsync($"/api/shops/{shop}/setup/sample"));
        var document = sample.GetProperty("document");
        Assert.Equal(expected, document.GetProperty("documentType").GetString());
        if (taxRate > 0)
        {
            var totals = document.GetProperty("totals");
            Assert.Equal(59m, totals.GetProperty("invoiceTotal").GetDecimal());
            Assert.Equal(TaxComponentKinds, totals.GetProperty("components").EnumerateArray()
                .Select(component => component.GetProperty("kind").GetString()));
        }
    }

    [Fact]
    public async Task SetupReads_RequireAuthenticationAndTheExactActiveShop()
    {
        using var anonymous = CreateClient();
        using var owner = CreateClient();
        var first = await CreateShopAsync(owner);
        var second = await CreateShopAsync(owner);
        using var unauthorized = await anonymous.GetAsync($"/api/shops/{second}/setup");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var wrongActiveShop = await owner.GetAsync($"/api/shops/{first}/setup");
        Assert.Equal(HttpStatusCode.Forbidden, wrongActiveShop.StatusCode);
        using var sample = await owner.GetAsync($"/api/shops/{first}/setup/sample");
        Assert.Equal(HttpStatusCode.Forbidden, sample.StatusCode);
        using var nonmember = CreateClient();
        _ = await CreateShopAsync(nonmember);
        using var forbidden = await nonmember.GetAsync($"/api/shops/{second}/setup");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private static async Task<Guid> AddServiceAsync(HttpClient client, string name = "Setup service", decimal taxRate = 0m, string? hsn = null)
    {
        var service = await ReadAsync(await client.PostAsJsonAsync("/api/services", new
        {
            name, price = 50m, taxRatePercent = taxRate, taxIncluded = false, hsnCode = hsn, isActive = true
        }));
        return service.GetProperty("serviceId").GetGuid();
    }

    private static async Task<Guid> ReceiveGoodsAsync(HttpClient client, string barcode, string uom, decimal quantity, DateOnly? expiryDate = null)
    {
        var receipt = await ReadAsync(await client.PostAsJsonAsync("/api/inventory/inbound", new
        {
            itemName = $"Goods {barcode}", barcode, uom, batchNumber = "setup-lot", quantity, totalPurchaseCost = quantity * 10m,
            mrp = 20m, salesPrice = 20m, taxRatePercent = 0m, taxIncluded = false, purchaseTaxIncluded = false, expiryDate
        }));
        return receipt.GetProperty("inventoryBatchId").GetGuid();
    }

    private static async Task<JsonElement> SellGoodsAsync(HttpClient client, Guid batch, string barcode, decimal quantity) =>
        await ReadAsync(await client.PostAsJsonAsync("/api/sales", new
        {
            idempotencyKey = Guid.NewGuid().ToString(),
            paymentMethod = "Cash",
            paidAmount = quantity * 20m,
            dueAmount = 0m,
            items = new[] { new { barcode, batchNumber = "setup-lot", itemName = $"Goods {barcode}",
                quantity, costPrice = 10m, salesPrice = 20m, mrp = 20m, taxRatePercent = 0m,
                isPriceIncludingTax = false, inventoryBatchId = batch } }
        }));

    private static Task<JsonElement> GetStatusAsync(HttpClient client, Guid shop) =>
        client.GetFromJsonAsync<JsonElement>($"/api/shops/{shop}/setup");

    private static string? Step(JsonElement status, string id) =>
        status.GetProperty("steps").EnumerateArray().Single(step => step.GetProperty("id").GetString() == id)
            .GetProperty("state").GetString();

    private static IEnumerable<string?> Reasons(JsonElement status) =>
        status.GetProperty("blockingReasons").EnumerateArray().Select(reason => reason.GetString());

    private async Task<string> SnapshotAsync(Guid shop) =>
        (await OnboardingBrowserProbe.ReadSnapshotAsync(_factory.MigratorConnectionString, shop)).GetRawText();
}
