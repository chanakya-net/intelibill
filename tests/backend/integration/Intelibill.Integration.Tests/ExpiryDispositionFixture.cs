using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using ClosedXML.Excel;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Integration.Tests;

public abstract class ExpiryDispositionFixture(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    protected string RuntimeConnectionString { get; } = fixture.RuntimeConnectionString;
    protected virtual ApiWebApplicationFactory Factory { get; } = new(fixture);
    public Task InitializeAsync() => Factory.InitializeAsync();
    public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
    public void Dispose() { Factory.Dispose(); GC.SuppressFinalize(this); }
    protected static DateOnly Today => BusinessDates.Today;
    protected sealed record Shop(HttpClient Client, string Token) : IDisposable { public void Dispose() => Client.Dispose(); }
    protected sealed record SoldSale(Guid SaleId, string InvoiceNumber, Guid LineId, Guid BatchId);

    protected Task<InventoryBatch> BatchAsync(SoldSale sale) => QueryAsync(db => db.InventoryBatches.Include(x => x.Item).SingleAsync(x => x.Id == sale.BatchId));
    protected Task<SaleReturn> ReturnAsync(Guid id) => QueryAsync(db => db.SaleReturns.Include(x => x.Items).SingleAsync(x => x.Id == id));
    protected async Task<(Guid Return, Guid Root, InventoryBatch Batch)> RecoverAsync(Shop shop, SoldSale sale, decimal quantity = 2m)
    {
        var id = await RecordReturnAsync(shop, sale, ReturnPayoutDestination.Refund, Line(sale.LineId, quantity: quantity));
        var line = (await ReturnAsync(id)).Items.Single();
        Assert.Equal(quantity, line.QuarantinedQuantity); Assert.NotNull(line.QuarantineChangeId);
        return (id, line.QuarantineChangeId!.Value, await BatchAsync(sale));
    }
    protected static Task<HttpResponseMessage> DisposeAsync(Shop shop, Guid batchId, Guid root, uint version, decimal quantity = 2m, string? key = null, string action = "Dispose") =>
        SendAsync(shop, HttpMethod.Post, $"/api/inventory/batches/{batchId}/dispositions", new { operationKey = key ?? Guid.NewGuid().ToString(), expectedVersion = version, quarantineChangeId = root, action, quantity, reason = "Recovered goods disposal" });
    protected static async Task AssertCodeAsync(HttpResponseMessage response, string code)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.False(response.IsSuccessStatusCode, content); Assert.Contains(code, content, StringComparison.Ordinal);
    }
    protected static async Task CorrectDateAsync(Shop shop, InventoryBatch batch)
    {
        using var response = await SendAsync(shop, HttpMethod.Put, $"/api/inventory/batches/{batch.Id}/prices", new {
            expectedVersion = batch.Version, batchNumber = batch.BatchNumber, batch.Mrp, batch.SalesPrice, batch.TaxRatePercent, batch.TaxIncluded,
            expiryDate = Today.AddDays(30), manufacturingDate = (DateOnly?)null, reason = "Corrected from original supplier evidence" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    protected static Task<HttpResponseMessage> SellCurrentAsync(Shop shop, InventoryBatch b, decimal quantity) =>
        SendAsync(shop,HttpMethod.Post,"/api/sales",new { idempotencyKey = Guid.NewGuid().ToString(), paymentMethod = 1,
            paidAmount = decimal.Round(quantity * 118m,2,MidpointRounding.AwayFromZero), dueAmount = 0m,
            items = new[] { new { barcode = b.Item.Barcode,batchNumber = b.BatchNumber,itemName = b.Item.Name,quantity,costPrice = 80m,salesPrice = 100m,
                mrp = 120m,taxRatePercent = 18m,isPriceIncludingTax = false,inventoryBatchId = b.Id } } });

    protected async Task<Shop> OpenShopAsync()
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        var enrolled = await client.EnrollAsync(new
        {
            email = $"return-accounting-{Guid.NewGuid():N}@test.com",
            password = "Pass123!Aa",
            firstName = "Return",
            lastName = "Owner",
        });
        enrolled.EnsureSuccessStatusCode();
        var token = (await enrolled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name = $"Shop {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
            gstNumber = TestTaxProfile.NewGstin(),
            taxProfile = TestTaxProfile.Regular,
        });
        using var created = await client.SendAsync(request);
        created.EnsureSuccessStatusCode();
        var shopToken = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        return new Shop(client, shopToken);
    }

    protected static async Task<HttpResponseMessage> SendAsync(Shop shop, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", shop.Token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await shop.Client.SendAsync(request);
    }

    protected static async Task<Guid> AddCustomerAsync(Shop shop)
    {
        using var customer = await SendAsync(shop, HttpMethod.Post, "/api/customers", new
        {
            name = "Due Customer",
            phoneNumber = "+919812345678",
            address = (string?)null,
            isActive = true,
        });
        customer.EnsureSuccessStatusCode();
        return (await customer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("customerId").GetGuid();
    }

    /// <summary>One goods line at the given price + 18% GST, paid in cash or partly on the customer's account.</summary>
    protected async Task<SoldSale> SellGoodsAsync(
        Shop shop,
        Guid? customerId = null,
        decimal? paid = null,
        decimal quantity = 1m,
        decimal salesPrice = 100m, bool missingDate = false, bool fractional = false)
    {
        var barcode = $"RA-{Guid.NewGuid():N}";
        var itemName = $"Return Item {barcode[3..11]}";
        var unitCost = Math.Round(salesPrice * 0.8m, 2, MidpointRounding.AwayFromZero);
        using var stock = await SendAsync(shop, HttpMethod.Post, "/api/inventory/inbound", new
        {
            itemName,
            barcode,
            itemDescription = (string?)null,
            hsnCode = (string?)null,
            uom = fractional ? "kg" : "pcs",
            quantityScale = fractional ? 3 : 0,
            batchNumber = "B-001",
            quantity = 10m,
            totalPurchaseCost = unitCost * 10m,
            mrp = 120m,
            salesPrice,
            taxRatePercent = 18m,
            taxIncluded = false,
            purchaseTaxIncluded = false,
            expiryDate = missingDate ? (DateOnly?)null : Today.AddDays(-1),
            manufacturingDate = (DateOnly?)null,
            supplierId = (Guid?)null,
            referenceNumber = (string?)null,
            notes = (string?)null,
            performedAt = (DateTimeOffset?)null,
        });
        stock.EnsureSuccessStatusCode();
        var batchId = (await stock.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("inventoryBatchId").GetGuid();

        var taxable = Math.Round(quantity * salesPrice, 2, MidpointRounding.AwayFromZero);
        var total = taxable + Math.Round(taxable * 0.18m, 2, MidpointRounding.AwayFromZero);
        var paidAmount = paid ?? total;
        using var response = await SendAsync(shop, HttpMethod.Post, "/api/sales", new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId,
            customerName = customerId.HasValue ? "Due Customer" : "Walk-in Customer",
            customerPhone = customerId.HasValue ? "+919812345678" : "+919876543210",
            paymentMethod = paidAmount < total ? (int)PaymentMethod.Credit : (int)PaymentMethod.Cash,
            paidAmount,
            dueAmount = total - paidAmount,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName,
                    quantity,
                    costPrice = unitCost,
                    salesPrice,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, content);
        var saleId = JsonSerializer.Deserialize<JsonElement>(content).GetProperty("saleId").GetGuid();

        return await QueryAsync(async db =>
        {
            var sale = await db.Sales.AsNoTracking().Include(s => s.Items).SingleAsync(s => s.Id == saleId);
            return new SoldSale(sale.Id, sale.InvoiceNumber, sale.Items.Single().Id, batchId);
        });
    }

    protected static object Line(Guid saleItemId, decimal? approvedRefundAmount = null, string? notes = null, decimal quantity = 1m) => new
    {
        saleItemId,
        quantity,
        lineType = (int)SaleLineType.Goods,
        condition = (int)SaleReturnCondition.Restockable,
        approvedRefundAmount,
        notes,
    };

    /// <summary>Records a return through the API and answers its id.</summary>
    protected async Task<Guid> RecordReturnAsync(Shop shop, SoldSale sale, ReturnPayoutDestination? destination, object line)
    {
        var earlier = await QueryAsync(db => db.SaleReturns.Where(r => r.SaleId == sale.SaleId).Select(r => r.Id).ToListAsync());
        using var response = await SendAsync(shop, HttpMethod.Post, $"/api/sales/{sale.SaleId}/returns", new
        {
            payoutDestination = (int?)destination,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = (string?)null,
            creditNoteReason = destination == ReturnPayoutDestination.CreditNote ? "Store credit" : null,
            items = new[] { line },
        });
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, content);

        return await QueryAsync(db => db.SaleReturns
            .Where(r => r.SaleId == sale.SaleId && !earlier.Contains(r.Id))
            .Select(r => r.Id)
            .SingleAsync());
    }

    protected async Task<T> QueryAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

}
