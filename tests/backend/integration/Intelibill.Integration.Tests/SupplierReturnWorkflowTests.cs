using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;
using Intelibill.Domain.Enums;
using Intelibill.Api.Controllers;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class SupplierReturnWorkflowTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    private HttpClient CreateClient() => _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static string UniqueEmail() => $"po-{Guid.NewGuid():N}@test.com";
    private static string UniquePhone() => $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}";

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.EnrollAsync(new
        {
            email = UniqueEmail(),
            password = "Pass123!Aa",
            firstName = "Test",
            lastName = "User",
            phoneNumber = UniquePhone(),
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

    private static async Task<Guid> CreateSupplierAsync(HttpClient client, string token, string name)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/suppliers");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name,
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pin = "560001",
            isActive = true,
            isPreferred = false,
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("supplierId").GetGuid();
    }

    private async Task<(HttpClient Client, Guid Supplier, Guid Receipt, Guid Batch)> SetupAsync()
    {
        var client=CreateClient();
        var token=await CreateShopAsync(client,await RegisterAsync(client));
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
        var supplier=await CreateSupplierAsync(client,token,"Return supplier");
        var inbound=await client.PostAsJsonAsync("/api/inventory/inbound",new {itemName="Return item",barcode=$"SR-{Guid.NewGuid():N}",uom="pcs",batchNumber=$"B-{Guid.NewGuid():N}",quantity=10,totalPurchaseCost=1000,mrp=150,salesPrice=120,supplierId=supplier});
        inbound.EnsureSuccessStatusCode();
        var b=await inbound.Content.ReadFromJsonAsync<JsonElement>();
        var batch=b.GetProperty("inventoryBatchId").GetGuid();
        using var scope=_factory.Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var receipt=await db.PurchaseReceiptValues.SingleAsync(x=>x.InventoryBatchId==batch);
        return(client,supplier,receipt.Id,batch);
    }
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    [Fact]
    public async Task PendingThenAccepted_OneCreditOnAcceptanceDate()
    {
        var x=await SetupAsync(); using var client=x.Client;
        var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));
        var key=Guid.NewGuid().ToString();
        var payload=new {operationKey=key,supplierId=x.Supplier,reason="Damaged packaging",creditAccepted=false,lines=new[]{new {receiptValueId=x.Receipt,expectedReceiptVersion=detail.GetProperty("receiptVersion").GetUInt32(),expectedBatchVersion=detail.GetProperty("batchVersion").GetUInt32(),quantity=3,proposedCreditAmount=300}}};
        var shipped=await JsonAsync(await client.PostAsJsonAsync("/api/supplier-returns",payload));
        var id=shipped.GetProperty("returnId").GetGuid();
        Assert.Equal("PendingSupplierCredit",shipped.GetProperty("status").GetString());
        var replay=await JsonAsync(await client.PostAsJsonAsync("/api/supplier-returns",payload)); Assert.Equal(id,replay.GetProperty("returnId").GetGuid());
        var after=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));
        Assert.Equal(7,after.GetProperty("availableQuantity").GetDecimal()); Assert.Equal(10,after.GetProperty("receivedQuantity").GetDecimal()); Assert.Equal(7,after.GetProperty("netRetainedQuantity").GetDecimal()); Assert.Equal(1000,after.GetProperty("netPayableAmount").GetDecimal());
        var credit=new {operationKey=Guid.NewGuid().ToString(),expectedVersion=shipped.GetProperty("version").GetUInt32(),reason="Supplier agreed",lines=new[]{new{returnLineId=shipped.GetProperty("lines")[0].GetProperty("returnLineId").GetGuid(),acceptedAmount=300}}};
        var accepted=await JsonAsync(await client.PostAsJsonAsync($"/api/supplier-returns/{id}/credit",credit)); Assert.Equal("CreditAccepted",accepted.GetProperty("status").GetString());
        after=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}")); Assert.Equal(700,after.GetProperty("netPayableAmount").GetDecimal()); Assert.Equal(7,after.GetProperty("availableQuantity").GetDecimal());
    }
    [Fact]
    public async Task UnsoldUnallocatedReceipt_ReversesExactEffectiveTotal()
    {
        var x=await SetupAsync(); using var client=x.Client;
        var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));
        var payload=new{operationKey=Guid.NewGuid().ToString(),reason="Wrong receipt",expectedReceiptVersion=detail.GetProperty("receiptVersion").GetUInt32(),expectedBatchVersion=detail.GetProperty("batchVersion").GetUInt32()};
        var reversed=await JsonAsync(await client.PostAsJsonAsync($"/api/purchase-receipts/{x.Receipt}/reversal",payload));
        Assert.Equal(1000,reversed.GetProperty("lots")[0].GetProperty("effectiveAmount").GetDecimal());
        var after=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}")); Assert.Equal(0,after.GetProperty("availableQuantity").GetDecimal()); Assert.Equal(0,after.GetProperty("netRetainedQuantity").GetDecimal());
    }

    private static object Shipment(JsonElement detail,Guid supplier,Guid receipt,string key,decimal quantity=3m,decimal credit=300m,bool accepted=false) => new{operationKey=key,supplierId=supplier,reason="Damaged goods",creditAccepted=accepted,lines=new[]{new{receiptValueId=receipt,expectedReceiptVersion=detail.GetProperty("receiptVersion").GetUInt32(),expectedBatchVersion=detail.GetProperty("batchVersion").GetUInt32(),quantity,proposedCreditAmount=credit}}};
    [Fact]
    public async Task PaidReceiptReturn_PreservesAllocationsAndShowsSupplierCredit()
    {
        var x=await SetupAsync();using var client=x.Client;
        await JsonAsync(await client.PostAsJsonAsync($"/api/suppliers/{x.Supplier}/payments",new{amount=1000,paymentDate=DateOnly.FromDateTime(DateTime.UtcNow),operationKey=Guid.NewGuid().ToString(),method="Cash",allocations=new[]{new{targetId=x.Receipt,amount=1000}}}));
        var before=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));
        await JsonAsync(await client.PostAsJsonAsync("/api/supplier-returns",Shipment(before,x.Supplier,x.Receipt,Guid.NewGuid().ToString(),accepted:true)));
        var after=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));
        Assert.Equal(1000,after.GetProperty("allocatedPaymentAmount").GetDecimal());Assert.Equal(700,after.GetProperty("netPayableAmount").GetDecimal());Assert.Equal(0,after.GetProperty("outstandingAmount").GetDecimal());Assert.Equal(300,after.GetProperty("supplierCreditAmount").GetDecimal());
        var balances=await JsonAsync(await client.GetAsync($"/api/payments/balances?partyType=Supplier&partyId={x.Supplier}"));
        var row=Assert.Single(balances.GetProperty("purchaseReceipts").EnumerateArray());Assert.Equal(300,row.GetProperty("supplierCreditAmount").GetDecimal());Assert.Equal(300,row.GetProperty("returnCreditAmount").GetDecimal());
    }
    [Theory]
    [InlineData(11,300)]
    [InlineData(0,300)]
    [InlineData(0.1,300)]
    [InlineData(3,-1)]
    [InlineData(3,0.001)]
    public async Task InvalidShipment_RollsBackAllFacts(decimal quantity,decimal credit)
    {
        var x=await SetupAsync();using var client=x.Client;
        var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));
        var response=await client.PostAsJsonAsync("/api/supplier-returns",Shipment(detail,x.Supplier,x.Receipt,Guid.NewGuid().ToString(),quantity,credit));Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        var after=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));Assert.Equal(10,after.GetProperty("availableQuantity").GetDecimal());Assert.Empty(after.GetProperty("returns").EnumerateArray());
    }
    [Fact]
    public async Task ChangedPayload_KeyConflict_AndShipmentBlocksOldVoidAndCostCorrection()
    {
        var x=await SetupAsync();using var client=x.Client;var key=Guid.NewGuid().ToString();
        var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));
        await JsonAsync(await client.PostAsJsonAsync("/api/supplier-returns",Shipment(detail,x.Supplier,x.Receipt,key)));
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/supplier-returns",Shipment(detail,x.Supplier,x.Receipt,key,2))).StatusCode);
        Assert.False((await client.PostAsJsonAsync($"/api/inventory/batches/{x.Batch}/void",new{})).IsSuccessStatusCode);
        var after=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));
        var correction=await client.PostAsJsonAsync($"/api/inventory/receipts/{x.Receipt}/cost-corrections",new{expectedVersion=after.GetProperty("receiptVersion").GetUInt32(),revisedTotalCost=900,reason="Correct cost"});
        Assert.Equal(HttpStatusCode.Conflict,correction.StatusCode);
    }
    [Fact]
    public async Task CrossShopHistory_IsHidden()
    {
        var x=await SetupAsync();using var client=x.Client;var other=await SetupAsync();using var foreign=other.Client;
        Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync($"/api/purchase-receipts/{x.Receipt}")).StatusCode);
        var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));
        var returned=await JsonAsync(await client.PostAsJsonAsync("/api/supplier-returns",Shipment(detail,x.Supplier,x.Receipt,Guid.NewGuid().ToString())));
        Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync($"/api/supplier-returns/{returned.GetProperty("returnId").GetGuid()}")).StatusCode);
    }
    [Fact]
    public async Task ConcurrentShipments_NoLostStock()
    {
        var x=await SetupAsync();using var client=x.Client;
        var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));
        var responses=await Task.WhenAll(client.PostAsJsonAsync("/api/supplier-returns",Shipment(detail,x.Supplier,x.Receipt,Guid.NewGuid().ToString(),7)),client.PostAsJsonAsync("/api/supplier-returns",Shipment(detail,x.Supplier,x.Receipt,Guid.NewGuid().ToString(),7)));
        Assert.Single(responses,r=>r.IsSuccessStatusCode);Assert.Single(responses,r=>r.StatusCode==HttpStatusCode.Conflict);
        var after=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));Assert.Equal(3,after.GetProperty("availableQuantity").GetDecimal());Assert.Single(after.GetProperty("returns").EnumerateArray());
    }
}
