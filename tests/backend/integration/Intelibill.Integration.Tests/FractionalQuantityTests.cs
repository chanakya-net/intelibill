using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class FractionalQuantityTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    [Fact]
    public async Task fractional_sale_return_reconciles_stock()
    {
        using var client = CreateClient();
        var token = await CreateShopTokenAsync(client);

        var kgBarcode = $"KG-{Guid.NewGuid():N}";
        var kgInbound = await ReceiveAsync(client, token, kgBarcode, "kg", 10m);
        var kgItemId = kgInbound.GetProperty("itemId").GetGuid();
        var kgBatchId = kgInbound.GetProperty("inventoryBatchId").GetGuid();

        var pcsBarcode = $"PC-{Guid.NewGuid():N}";
        var pcsInbound = await ReceiveAsync(client, token, pcsBarcode, "pcs", 10m);
        var pcsBatchId = pcsInbound.GetProperty("inventoryBatchId").GetGuid();

        // 0.25 kg: stock 9.75, the issued document prints KGS with the server's totals.
        var sale = await SellAsync(client, token, kgBarcode, "B-kg", kgBatchId, 0.25m, 29.5m);
        Assert.Equal(HttpStatusCode.Created, sale.StatusCode);
        var saleBody = await sale.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();
        Assert.Equal(29.5m, saleBody.GetProperty("totalAmount").GetDecimal());

        Assert.Equal(9.75m, await StockAsync(kgItemId));

        using var detailRequest = Authorized(HttpMethod.Get, $"/api/sales/{saleId}", token);
        var detailResponse = await client.SendAsync(detailRequest);
        detailResponse.EnsureSuccessStatusCode();
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        var line = detail.GetProperty("document").GetProperty("lines").EnumerateArray().Single();
        Assert.Equal("KGS", line.GetProperty("uom").GetString());
        Assert.Equal(0.25m, line.GetProperty("quantity").GetDecimal());
        Assert.Equal(29.5m, detail.GetProperty("document").GetProperty("totals").GetProperty("invoiceTotal").GetDecimal());

        // Return 0.10 kg: stock 9.85.
        Guid saleItemId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            saleItemId = (await db.Sales.Include(s => s.Items).FirstAsync(s => s.Id == saleId)).Items.Single().Id;
        }

        using var returnRequest = Authorized(HttpMethod.Post, $"/api/sales/{saleId}/returns", token);
        returnRequest.Content = JsonContent.Create(new
        {
            payoutMethod = (int)PaymentMethod.Cash,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Partial weight return",
            items = new[]
            {
                new
                {
                    saleItemId,
                    quantity = 0.10m,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = 11.8m,
                    notes = (string?)null,
                },
            },
        });
        var returnResponse = await client.SendAsync(returnRequest);
        Assert.Equal(HttpStatusCode.OK, returnResponse.StatusCode);
        Assert.Equal(9.85m, await StockAsync(kgItemId));

        // Half a whole-unit item and a fourth decimal are refused.
        var half = await SellAsync(client, token, pcsBarcode, "B-pcs", pcsBatchId, 0.5m, 59m);
        Assert.Equal(HttpStatusCode.BadRequest, half.StatusCode);
        Assert.Contains("Sale.QuantityPrecisionExceeded", await ErrorCodesAsync(half));

        var fine = await SellAsync(client, token, kgBarcode, "B-kg", kgBatchId, 0.0001m, 1m);
        Assert.Equal(HttpStatusCode.BadRequest, fine.StatusCode);
        Assert.Contains("Sale.QuantityPrecisionExceeded", await ErrorCodesAsync(fine));
        Assert.Equal(9.85m, await StockAsync(kgItemId));

        // Changing the unit after stock history is refused; the document still prints KGS.
        using var patch = Authorized(HttpMethod.Patch, $"/api/items/{kgItemId}", token);
        patch.Content = JsonContent.Create(new
        {
            name = "Item kg",
            barcode = kgBarcode,
            description = (string?)null,
            uom = "pcs",
            quantityScale = 0,
            hsnCode = (string?)null,
            defaultTaxRatePercent = 18m,
        });
        var patchResponse = await client.SendAsync(patch);
        Assert.Equal(HttpStatusCode.Conflict, patchResponse.StatusCode);
        Assert.Contains("Item.UnitChangeWithHistory", await ErrorCodesAsync(patchResponse));

        using var again = Authorized(HttpMethod.Get, $"/api/sales/{saleId}", token);
        var againBody = await (await client.SendAsync(again)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "KGS",
            againBody.GetProperty("document").GetProperty("lines").EnumerateArray().Single().GetProperty("uom").GetString());
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<string> ErrorCodesAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var codes = new List<string>();
        if (body.TryGetProperty("title", out var title) && title.GetString() is { } t)
        {
            codes.Add(t);
        }

        if (body.TryGetProperty("code", out var code) && code.GetString() is { } c)
        {
            codes.Add(c);
        }

        if (body.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
        {
            codes.AddRange(errors.EnumerateArray()
                .Where(e => e.TryGetProperty("code", out _))
                .Select(e => e.GetProperty("code").GetString() ?? string.Empty));
        }

        return string.Join("|", codes);
    }

    private async Task<decimal> StockAsync(Guid itemId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Inventory.IgnoreQueryFilters().Where(i => i.ItemId == itemId).Select(i => i.Quantity).SingleAsync();
    }

    private static async Task<string> CreateShopTokenAsync(HttpClient client)
    {
        var enroll = await client.EnrollAsync(new
        {
            email = $"fractional-{Guid.NewGuid():N}@test.com",
            password = "Pass123!Aa",
            firstName = "Test",
            lastName = "User",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}",
        });
        enroll.EnsureSuccessStatusCode();
        var enrolled = await enroll.Content.ReadFromJsonAsync<JsonElement>();
        var token = enrolled.GetProperty("accessToken").GetString()!;

        using var request = Authorized(HttpMethod.Post, "/api/shops", token);
        request.Content = JsonContent.Create(new
        {
            name = $"Shop {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
            gstNumber = TestTaxProfile.NewGstin(),
            taxProfile = new { stateCode = "29", taxRegime = "Regular", eInvoiceStatus = "BelowThreshold", invoiceSeriesCode = "A" },
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static async Task<JsonElement> ReceiveAsync(
        HttpClient client, string token, string barcode, string uom, decimal quantity)
    {
        using var request = Authorized(HttpMethod.Post, "/api/inventory/inbound", token);
        request.Content = JsonContent.Create(new
        {
            itemName = $"Item {uom}",
            barcode,
            itemDescription = (string?)null,
            hsnCode = (string?)null,
            uom,
            batchNumber = $"B-{uom}",
            quantity,
            totalPurchaseCost = 80m * quantity,
            mrp = 120m,
            salesPrice = 100m,
            taxRatePercent = 18m,
            taxIncluded = false,
            purchaseTaxIncluded = false,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId = (Guid?)null,
            referenceNumber = (string?)null,
            notes = (string?)null,
            performedAt = (DateTimeOffset?)null,
        });
        var response = await client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<HttpResponseMessage> SellAsync(
        HttpClient client, string token, string barcode, string batchNumber, Guid batchId, decimal quantity, decimal paid)
    {
        var request = Authorized(HttpMethod.Post, "/api/sales", token);
        request.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Walk-in Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = paid,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber,
                    itemName = $"Item {batchNumber[2..]}",
                    quantity,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });
        return client.SendAsync(request);
    }
}
