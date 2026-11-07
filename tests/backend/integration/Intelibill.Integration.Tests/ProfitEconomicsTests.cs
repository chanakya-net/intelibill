using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Intelibill.Domain.Common;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class ProfitEconomicsTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory _factory = new(fixture);
    private static DateOnly Today => BusinessCalendar.DateOf(DateTimeOffset.UtcNow);
    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() { Dispose(); return Task.CompletedTask; }
    public void Dispose() { _factory.Dispose(); GC.SuppressFinalize(this); }

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token, HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task<(Guid ShopId, string Token)> ShopAsync(HttpClient client)
    {
        using var enrolled = await client.EnrollAsync(new { email = $"economics-{Guid.NewGuid():N}@test.com", password = "Pass123!Aa", firstName = "Profit", lastName = "Tester", phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}" });
        var identity = await ReadAsync(enrolled);
        var shop = await ReadAsync(await SendAsync(client, identity.GetProperty("accessToken").GetString()!, HttpMethod.Post, "/api/shops", new {
            name = $"Economics {Guid.NewGuid():N}", address = "42 Market Road", city = "Bengaluru", state = "Karnataka", pincode = "560001", gstNumber = TestTaxProfile.NewGstin(), taxProfile = TestTaxProfile.Regular }));
        return (shop.GetProperty("activeShopId").GetGuid(), shop.GetProperty("accessToken").GetString()!);
    }
    private static async Task<JsonElement> EconomicsAsync(HttpClient client, string token, string? range = null)
    {
        var report = await ReadAsync(await SendAsync(client, token, HttpMethod.Get, "/api/sales/profit-loss" + (range ?? $"?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}")));
        return report.GetProperty("summary").GetProperty("economics");
    }
    private async Task<T> QueryAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    [Fact]
    public async Task RecordedCost_WorkflowAgreesAcrossPreviewReportWorkbookAndRestrictedTenants()
    {
        using var client = Client();
        var shop = await ShopAsync(client);
        var supplier = await ReadAsync(await SendAsync(client, shop.Token, HttpMethod.Post, "/api/suppliers", new {
            name = "Supplier", contactPerson = "Contact", contactPhone = "+919876543211", address = "Market", city = "Bengaluru", state = "Karnataka", pin = "560002", isPreferred = true }));
        var supplierId = supplier.GetProperty("supplierId").GetGuid();
        var barcode = "ECON-" + Guid.NewGuid().ToString("N");
        await ReadAsync(await SendAsync(client, shop.Token, HttpMethod.Post, "/api/items", new { name = "Goods", barcode, uom = "KG", quantityScale = 3, supplierId, isActive = true }));
        var intake = await ReadAsync(await SendAsync(client, shop.Token, HttpMethod.Post, "/api/inventory/inbound", new {
            itemName = "Goods", barcode, uom = "KG", batchNumber = "ECON", quantity = 10m, totalPurchaseCost = 1000m,
            salesPrice = 236m, mrp = 250m, taxRatePercent = 18m, taxIncluded = true, purchaseTaxIncluded = true, supplierId,
            referenceNumber = "Economic stock", performedAt = DateTimeOffset.UtcNow }));
        var batchId = intake.GetProperty("inventoryBatchId").GetGuid();
        object Line(decimal discount) => new { barcode, batchNumber = "ECON", itemName = "Goods", quantity = 1m,
            costPrice = 0m, salesPrice = 236m, mrp = 250m, taxRatePercent = 18m, isPriceIncludingTax = true,
            inventoryBatchId = batchId, itemDiscount = new { type = discount == 0m ? 0 : 2, value = discount }, clientLineKey = "one" };
        using var refused = await SendAsync(client, shop.Token, HttpMethod.Post, "/api/sales/preview", new { saleDiscount = new { type = 0, value = 0m }, items = new[] { Line(110m) } });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("90.00", await refused.Content.ReadAsStringAsync());
        Assert.Contains("100.00", await refused.Content.ReadAsStringAsync());
        var preview = await ReadAsync(await SendAsync(client, shop.Token, HttpMethod.Post, "/api/sales/preview", new { saleDiscount = new { type = 0, value = 0m }, items = new[] { Line(0m) } }));
        var previewEconomics = preview.GetProperty("lines")[0].GetProperty("economics");
        Assert.Equal(100m, previewEconomics.GetProperty("economicCost").GetDecimal());
        Assert.Equal(100m, previewEconomics.GetProperty("grossProfit").GetDecimal());
        var sale = await ReadAsync(await SendAsync(client, shop.Token, HttpMethod.Post, "/api/sales", new {
            idempotencyKey = Guid.NewGuid().ToString("N"), paymentMethod = 1, paidAmount = 236m, dueAmount = 0m, items = new[] { Line(0m) } }));
        var saleId = sale.GetProperty("saleId").GetGuid();
        var saleItem = await QueryAsync(db => db.SaleItems.Where(i => i.SaleId == saleId).SingleAsync());
        Assert.Equal(100m, saleItem.CostPrice);
        Assert.Equal(200m, saleItem.TaxableAmount);
        var expense = await ReadAsync(await SendAsync(client, shop.Token, HttpMethod.Post, "/api/expenses", new {
            categoryName = "Rent", amount = 20m, paidTo = "Landlord", expenseDate = Today, treatment = "Operating" }));
        await ReadAsync(await SendAsync(client, shop.Token, HttpMethod.Post, "/api/expenses", new {
            categoryName = "Equipment", amount = 50m, paidTo = "Vendor", expenseDate = Today, treatment = "CapitalOrOtherExcluded" }));
        var paid = await ReadAsync(await SendAsync(client, shop.Token, HttpMethod.Post, $"/api/suppliers/{supplierId}/payments", new {
            amount = 100m, paymentDate = Today, operationKey = Guid.NewGuid().ToString("N"), method = "Cash" }));
        var receiptId = paid.GetProperty("receiptId").GetGuid();
        var linkedExpense = (await QueryAsync(db => db.PaymentReceipts.Where(r => r.Id == receiptId).Select(r => r.ExpenseId).SingleAsync()))!.Value;
        for (var generation = 0; generation < 2; generation++)
        {
            var corrected = await ReadAsync(await SendAsync(client, shop.Token, HttpMethod.Post, $"/api/expenses/{linkedExpense}/correct", new {
                categoryName = "Misleading category", amount = 100m, paidTo = "Supplier", description = "Corrected description", expenseDate = Today }));
            Assert.Equal("SupplierSettlement", corrected.GetProperty("treatment").GetString());
            linkedExpense = corrected.GetProperty("id").GetGuid();
        }
        var economics = await EconomicsAsync(client, shop.Token);
        Assert.Equal(200m, economics.GetProperty("netSalesExcludingTax").GetDecimal());
        Assert.Equal(100m, economics.GetProperty("cogs").GetDecimal());
        Assert.Equal(100m, economics.GetProperty("grossTradingProfit").GetDecimal());
        Assert.Equal(20m, economics.GetProperty("operatingExpenses").GetDecimal());
        Assert.Equal(80m, economics.GetProperty("operatingProfit").GetDecimal());
        Assert.Equal(50m, economics.GetProperty("grossMarginPercent").GetDecimal());
        Assert.Equal(100m, economics.GetProperty("markupPercent").GetDecimal());
        var searched = await ReadAsync(await SendAsync(client, shop.Token, HttpMethod.Get, $"/api/sales/profit-loss?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&search=200"));
        Assert.Equal(1, searched.GetProperty("totalCount").GetInt32());
        Assert.Equal("FilteredActivity", searched.GetProperty("summary").GetProperty("economics").GetProperty("operatingProfitStatus").GetString());
        Assert.Equal(JsonValueKind.Null, searched.GetProperty("summary").GetProperty("economics").GetProperty("operatingExpenses").ValueKind);
        var searchedExport = await SendAsync(client, shop.Token, HttpMethod.Get, $"/api/exports/profit-loss?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&search=200&format=xlsx");
        using (var book = new XLWorkbook(new MemoryStream(await searchedExport.Content.ReadAsByteArrayAsync())))
        {
            var sheet = book.Worksheet("Profit & Loss");
            var header = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == "Reference").RowNumber();
            Assert.Equal(sale.GetProperty("invoiceNumber").GetString(), sheet.Cell(header + 1, 1).GetString());
            Assert.Equal("Not available", sheet.RowsUsed().Single(r => r.Cell(1).GetString() == "Operating expenses").Cell(2).GetString());
        }
        var workbookResponse = await SendAsync(client, shop.Token, HttpMethod.Get, $"/api/exports/profit-loss?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&format=xlsx");
        Assert.True(workbookResponse.IsSuccessStatusCode);
        using (var workbook = new XLWorkbook(new MemoryStream(await workbookResponse.Content.ReadAsByteArrayAsync())))
        {
            var sheet = workbook.Worksheet("Profit & Loss");
            Assert.Equal(80m, sheet.RowsUsed().Single(r => r.Cell(1).GetString() == "Operating profit").Cell(2).GetValue<decimal>());
        }
        // Requests use the production-shaped DML-only database role, never table-owner bypass.
        await using (var connection = new NpgsqlConnection(_factory.RuntimeConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("select rolsuper or rolbypassrls from pg_roles where rolname = current_user", connection);
            Assert.False((bool)(await command.ExecuteScalarAsync())!);
        }
        var other = await ShopAsync(client);
        await ReadAsync(await SendAsync(client, other.Token, HttpMethod.Post, "/api/expenses", new { categoryName = "Foreign rent", amount = 999m, paidTo = "Foreign", expenseDate = Today, treatment = "Operating" }));
        Assert.Equal(80m, (await EconomicsAsync(client, shop.Token)).GetProperty("operatingProfit").GetDecimal());
        Assert.Equal(0m, (await EconomicsAsync(client, other.Token)).GetProperty("grossTradingProfit").GetDecimal());
        await ReadAsync(await SendAsync(client, shop.Token, HttpMethod.Post, $"/api/payments/{receiptId}/reversal", new { reason = "Payment cancelled", operationKey = Guid.NewGuid().ToString("N") }));
        Assert.True(await QueryAsync(db => db.Expenses.Where(e => e.Id == linkedExpense).Select(e => e.IsVoided).SingleAsync()));
        Assert.Equal(80m, (await EconomicsAsync(client, shop.Token)).GetProperty("operatingProfit").GetDecimal());
        // Replacement expense facts change only their own amount; supplier settlement remains excluded.
        await ReadAsync(await SendAsync(client, shop.Token, HttpMethod.Post, $"/api/expenses/{expense.GetProperty("id").GetGuid()}/correct", new {
            categoryName = "Rent", amount = 30m, paidTo = "Landlord", expenseDate = Today }));
        Assert.Equal(70m, (await EconomicsAsync(client, shop.Token)).GetProperty("operatingProfit").GetDecimal());
        foreach (var condition in new[] { 1, 2 })
        {
            await ReadAsync(await SendAsync(client, shop.Token, HttpMethod.Post, $"/api/sales/{saleId}/returns", new {
                payoutDestination = 2, notes = "Return half", items = new[] { new { saleItemId = saleItem.Id, quantity = .5m, condition, approvedRefundAmount = 118m, notes = "Returned half: damaged if wastage" } } }));
        }
        economics = await EconomicsAsync(client, shop.Token);
        Assert.Equal(0m, economics.GetProperty("netSalesExcludingTax").GetDecimal());
        Assert.Equal(50m, economics.GetProperty("cogs").GetDecimal());
        Assert.Equal(50m, economics.GetProperty("returnedGoodsWastageIncludedInCogs").GetDecimal());
        Assert.Equal(0m, economics.GetProperty("stockLoss").GetDecimal());
        Assert.Equal(-50m, economics.GetProperty("grossTradingProfit").GetDecimal());
        var adjustment = await ReadAsync(await SendAsync(client, shop.Token, HttpMethod.Post, $"/api/inventory/batches/{batchId}/adjust", new {
            direction = "Decrease", reason = "Damaged", quantity = 1m, notes = "Independent loss", performedAt = new DateTimeOffset(2026, 6, 30, 18, 29, 59, TimeSpan.Zero) }));
        await ReadAsync(await SendAsync(client, shop.Token, HttpMethod.Post, $"/api/inventory/adjustments/{adjustment.GetProperty("adjustmentId").GetGuid()}/void", new { reason = "Count corrected" }));
        Assert.Equal(100m, (await EconomicsAsync(client, shop.Token, "?from=2026-06-01&to=2026-06-30")).GetProperty("stockLoss").GetDecimal());
        Assert.Equal(-100m, (await EconomicsAsync(client, shop.Token)).GetProperty("stockLoss").GetDecimal());
        var fractionalStock = await ReadAsync(await SendAsync(client, shop.Token, HttpMethod.Post, "/api/inventory/inbound", new {
            itemName = "Goods", barcode, uom = "KG", batchNumber = "FRACTION", quantity = 10m, totalPurchaseCost = 1000.50m,
            salesPrice = 236m, mrp = 250m, taxRatePercent = 18m, taxIncluded = true, supplierId, performedAt = DateTimeOffset.UtcNow }));
        var fractionalSale = await ReadAsync(await SendAsync(client, shop.Token, HttpMethod.Post, "/api/sales", new {
            idempotencyKey = Guid.NewGuid().ToString("N"), paymentMethod = 1, paidAmount = 354m, dueAmount = 0m,
            items = new[] { new { barcode, batchNumber = "FRACTION", itemName = "Goods", quantity = 1.5m, costPrice = 0m, salesPrice = 236m,
                mrp = 250m, taxRatePercent = 18m, isPriceIncludingTax = true, inventoryBatchId = fractionalStock.GetProperty("inventoryBatchId").GetGuid() } } }));
        var fractionalSearch = await ReadAsync(await SendAsync(client, shop.Token, HttpMethod.Get, $"/api/sales/profit-loss?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&search=150.08"));
        Assert.Equal(1, fractionalSearch.GetProperty("totalCount").GetInt32());
        Assert.Equal(150.08m, fractionalSearch.GetProperty("items")[0].GetProperty("economics").GetProperty("cogs").GetDecimal());
        var fractionalExport = await SendAsync(client, shop.Token, HttpMethod.Get, $"/api/exports/profit-loss?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&search=150.08&format=xlsx");
        using var fractionalBook = new XLWorkbook(new MemoryStream(await fractionalExport.Content.ReadAsByteArrayAsync()));
        var fractionalSheet = fractionalBook.Worksheet("Profit & Loss");
        var fractionalHeader = fractionalSheet.RowsUsed().Single(r => r.Cell(1).GetString() == "Reference").RowNumber();
        Assert.Equal(fractionalSale.GetProperty("invoiceNumber").GetString(), fractionalSheet.Cell(fractionalHeader + 1, 1).GetString());
        Assert.Equal(150.08m, fractionalSheet.Cell(fractionalHeader + 1, 5).GetValue<decimal>());
        Assert.Equal("Not available", fractionalSheet.RowsUsed().Single(r => r.Cell(1).GetString() == "Operating expenses").Cell(2).GetString());

    }
}
