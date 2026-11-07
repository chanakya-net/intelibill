using Intelibill.Domain.Entities;
using Npgsql;
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
public sealed class SupplierReturnWorkflowTests : IAsyncLifetime, IDisposable
{
    private readonly CapturingStockUpdates _updates=new();
    private readonly ApiWebApplicationFactory _factory;
    public SupplierReturnWorkflowTests(PostgreSqlTestFixture fixture)
    {
        _factory=new(fixture,services=>services.AddSingleton<Intelibill.Application.Common.Interfaces.IShopUpdatesNotifier>(_updates));
    }
    private sealed class CapturingStockUpdates : Intelibill.Application.Common.Interfaces.IShopUpdatesNotifier
    {
        public System.Threading.Channels.Channel<(Guid ShopId,IReadOnlyCollection<Guid> Items)> Queue {get;}=System.Threading.Channels.Channel.CreateUnbounded<(Guid,IReadOnlyCollection<Guid>)>();
        public int Count {get;private set;}
        public Func<Guid,Task>? ObserveCommitted {get;set;}
        public async Task NotifyShopUpdateAsync(string eventType,Guid shopId,IReadOnlyCollection<Guid> changedIds,DateTimeOffset occurredOn,CancellationToken cancellationToken=default)
        {
            if(eventType!="InventoryStockChanged")return;
            if(ObserveCommitted is not null)await ObserveCommitted(shopId);
            Count++;
            await Queue.Writer.WriteAsync((shopId,changedIds),cancellationToken);
        }
    }

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
            gstNumber = TestTaxProfile.NewGstin(),
            taxProfile = TestTaxProfile.Regular,
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

    private async Task<(HttpClient Client, Guid Supplier, Guid Receipt, Guid Batch)> SetupAsync(decimal cost = 1000m,DateTimeOffset? performedAt = null)
    {
        var client=CreateClient();
        var token=await CreateShopAsync(client,await RegisterAsync(client));
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
        var supplier=await CreateSupplierAsync(client,token,"Return supplier");
        var inbound=await client.PostAsJsonAsync("/api/inventory/inbound",new {itemName="Return item",barcode=$"SR-{Guid.NewGuid():N}",uom="pcs",batchNumber=$"B-{Guid.NewGuid():N}",quantity=10,totalPurchaseCost=cost,performedAt,mrp=150,salesPrice=120,supplierId=supplier});
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
        var after=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}")); Assert.Equal(0,after.GetProperty("availableQuantity").GetDecimal()); Assert.Equal(0,after.GetProperty("netRetainedQuantity").GetDecimal());Assert.Equal(0,after.GetProperty("netPayableAmount").GetDecimal());Assert.Equal(0,after.GetProperty("outstandingAmount").GetDecimal());Assert.Equal(1000,after.GetProperty("effectiveTotalCost").GetDecimal());
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

    [Fact]
    public async Task CountHistory_OldVoidAndFullReversalCannotBypass()
    {
        var x=await SetupAsync();using var client=x.Client;
        var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));
        var count=await client.PostAsJsonAsync($"/api/inventory/batches/{x.Batch}/count",new{expectedVersion=detail.GetProperty("batchVersion").GetUInt32(),countedQuantity=10,reason="Confirmed stock"});
        await JsonAsync(count);
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync($"/api/inventory/batches/{x.Batch}/void",new{})).StatusCode);
    }

    [Fact]
    public async Task ManagerPendingOnly_AndRoleRevokedBeforeReplayDenied()
    {
        var x=await SetupAsync();using var client=x.Client;string? email;
        using(var scope=_factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var receipt=await db.PurchaseReceiptValues.SingleAsync(r=>r.Id==x.Receipt);var membership=await db.ShopMemberships.SingleAsync(m=>m.ShopId==receipt.ShopId);membership.SetRole(ShopRole.Manager);email=(await db.Users.SingleAsync(u=>u.Id==membership.UserId)).Email;await db.SaveChangesAsync();
        }
        var login=await JsonAsync(await client.PostAsJsonAsync("/api/auth/login/email",new{email,password="Pass123!Aa"}));client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",login.GetProperty("accessToken").GetString());
        var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));var key=Guid.NewGuid().ToString();var pending=Shipment(detail,x.Supplier,x.Receipt,key);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync("/api/supplier-returns",Shipment(detail,x.Supplier,x.Receipt,Guid.NewGuid().ToString(),accepted:true))).StatusCode);
        var result=await JsonAsync(await client.PostAsJsonAsync("/api/supplier-returns",pending));
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync($"/api/supplier-returns/{result.GetProperty("returnId").GetGuid()}/credit",new{operationKey=Guid.NewGuid().ToString(),expectedVersion=result.GetProperty("version").GetUInt32(),reason="Agreed credit",lines=new[]{new{returnLineId=result.GetProperty("lines")[0].GetProperty("returnLineId").GetGuid(),acceptedAmount=300}}})).StatusCode);
        using(var scope=_factory.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var receipt=await db.PurchaseReceiptValues.SingleAsync(r=>r.Id==x.Receipt);var membership=await db.ShopMemberships.SingleAsync(m=>m.ShopId==receipt.ShopId);membership.SetRole(ShopRole.Staff);await db.SaveChangesAsync();}
        Assert.Contains((await client.PostAsJsonAsync("/api/supplier-returns",pending)).StatusCode,new[]{HttpStatusCode.Forbidden,HttpStatusCode.Unauthorized});
    }
    [Fact]
    public async Task ZeroCredit_HasEvidenceWithoutLedger_AndCannotAcceptTwice()
    {
        var x=await SetupAsync();using var client=x.Client;var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));
        var result=await JsonAsync(await client.PostAsJsonAsync("/api/supplier-returns",Shipment(detail,x.Supplier,x.Receipt,Guid.NewGuid().ToString(),credit:0,accepted:true)));
        Assert.Equal("CreditAccepted",result.GetProperty("status").GetString());Assert.Equal(0,result.GetProperty("lines")[0].GetProperty("acceptedAmount").GetDecimal());Assert.Equal(JsonValueKind.Null,result.GetProperty("lines")[0].GetProperty("supplierLedgerEntryId").ValueKind);
        var response=await client.PostAsJsonAsync($"/api/supplier-returns/{result.GetProperty("returnId").GetGuid()}/credit",new{operationKey=Guid.NewGuid().ToString(),expectedVersion=result.GetProperty("version").GetUInt32(),reason="Second agreement",lines=new[]{new{returnLineId=result.GetProperty("lines")[0].GetProperty("returnLineId").GetGuid(),acceptedAmount=0}}});Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
    }
    [Fact]
    public async Task CountAddedQuantity_DoesNotRaiseReceiptCeiling()
    {
        var x=await SetupAsync();using var client=x.Client;var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));
        await JsonAsync(await client.PostAsJsonAsync($"/api/inventory/batches/{x.Batch}/count",new{expectedVersion=detail.GetProperty("batchVersion").GetUInt32(),countedQuantity=20,reason="Found more stock"}));
        detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));Assert.Equal(10,detail.GetProperty("returnableQuantity").GetDecimal());
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/supplier-returns",Shipment(detail,x.Supplier,x.Receipt,Guid.NewGuid().ToString(),11))).StatusCode);
    }
    [Fact]
    public async Task FullReversal_UsesCorrectedExactTotal_AndReplaysOnce()
    {
        var x=await SetupAsync();using var client=x.Client;var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));
        await JsonAsync(await client.PostAsJsonAsync($"/api/inventory/receipts/{x.Receipt}/cost-corrections",new{expectedVersion=detail.GetProperty("receiptVersion").GetUInt32(),revisedTotalCost=1100,reason="Invoice correction"}));
        detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));var payload=new{operationKey=Guid.NewGuid().ToString(),reason="Wrong receipt",expectedReceiptVersion=detail.GetProperty("receiptVersion").GetUInt32(),expectedBatchVersion=detail.GetProperty("batchVersion").GetUInt32()};
        var result=await JsonAsync(await client.PostAsJsonAsync($"/api/purchase-receipts/{x.Receipt}/reversal",payload));Assert.Equal(1100,result.GetProperty("lots")[0].GetProperty("effectiveAmount").GetDecimal());
        var replay=await JsonAsync(await client.PostAsJsonAsync($"/api/purchase-receipts/{x.Receipt}/reversal",payload));Assert.Equal(result.GetRawText(),replay.GetRawText());
        var after=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));Assert.Single(after.GetProperty("reversals").EnumerateArray());Assert.Equal(1100,after.GetProperty("effectiveTotalCost").GetDecimal());
    }
    [Fact]
    public async Task Snapshot_SurvivesSupplierAndItemRename()
    {
        var x=await SetupAsync();using var client=x.Client;var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));
        var document=await JsonAsync(await client.PostAsJsonAsync("/api/supplier-returns",Shipment(detail,x.Supplier,x.Receipt,Guid.NewGuid().ToString())));
        using(var scope=_factory.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var supplier=await db.Suppliers.SingleAsync(s=>s.Id==x.Supplier);supplier.Update("Renamed supplier",null,null,null,null,null,null,true,false);var batch=await db.InventoryBatches.Include(b=>b.Item).SingleAsync(b=>b.Id==x.Batch);batch.ReviseRetailTerms("Renamed live batch",batch.Mrp,batch.SalesPrice,batch.TaxRatePercent,batch.TaxIncluded,batch.ExpiryDate,batch.ManufacturingDate,batch.CreatedBy);batch.Item.Update("Renamed live item",batch.Item.Description,batch.Item.Barcode,true,batch.CreatedBy);await db.SaveChangesAsync();}
        var reloaded=await JsonAsync(await client.GetAsync($"/api/supplier-returns/{document.GetProperty("returnId").GetGuid()}"));Assert.Equal("Return supplier",reloaded.GetProperty("supplierName").GetString());Assert.Equal("Return item",reloaded.GetProperty("lines")[0].GetProperty("itemName").GetString());Assert.Equal("NOS",reloaded.GetProperty("lines")[0].GetProperty("uqc").GetString());Assert.Equal(detail.GetProperty("batchNumber").GetString(),reloaded.GetProperty("lines")[0].GetProperty("batchNumberSnapshot").GetString());
        var history=await JsonAsync(await client.GetAsync($"/api/supplier-returns?supplierId={x.Supplier}&status=PendingSupplierCredit"));Assert.Single(history.EnumerateArray());
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    public async Task GroupReversal_ExactLotsAtomic_AndPoReceivedRemainsHistorical(bool shipFirst,bool missingSource)
    {
        var x=await SetupAsync();using var client=x.Client;
        Guid itemId;string barcode;
        using(var scope=_factory.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var source=await db.PurchaseReceiptValues.SingleAsync(r=>r.Id==x.Receipt);var item=await db.Items.SingleAsync(it=>it.Id==source.ItemId);itemId=item.Id;barcode=item.Barcode;}
        var draft=await JsonAsync(await client.PostAsJsonAsync("/api/purchase-orders",new{supplierId=x.Supplier,lines=new[]{new{itemId,description="PO goods",expectedQuantity=10,unitCost=100}}}));var poId=draft.GetProperty("purchaseOrderId").GetGuid();var poLineId=draft.GetProperty("lines")[0].GetProperty("lineId").GetGuid();
        await JsonAsync(await client.PostAsJsonAsync($"/api/purchase-orders/{poId}/place",new{}));
        object Lot(int quantity)=>new{clientLineId=Guid.NewGuid(),purchaseOrderLineId=poLineId,barcode,batchNumber=$"PO-{Guid.NewGuid():N}",quantity,totalPurchaseCost=quantity*100,mrp=150,salesPrice=120,taxRatePercent=0,taxIncluded=false,purchaseTaxIncluded=false};
        await JsonAsync(await client.PostAsJsonAsync($"/api/purchase-orders/{poId}/receipts",new{operationId=Guid.NewGuid(),lines=new[]{Lot(4),Lot(6)}}));
        var po=await JsonAsync(await client.GetAsync($"/api/purchase-orders/{poId}"));var group=po.GetProperty("receipts")[0];var groupId=group.GetProperty("receiptId").GetGuid();var sourceIds=group.GetProperty("lines").EnumerateArray().Select(l=>l.GetProperty("receiptValueId").GetGuid()).ToArray();Assert.Equal(2,sourceIds.Length);
        if(missingSource){using var scope=_factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var missing=await db.PurchaseReceiptValues.SingleAsync(r=>r.Id==sourceIds[1]);db.PurchaseReceiptValues.Remove(missing);await db.SaveChangesAsync();sourceIds=sourceIds.Take(1).ToArray();}
        if(shipFirst){var first=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{sourceIds[0]}"));var document=await JsonAsync(await client.PostAsJsonAsync("/api/supplier-returns",Shipment(first,x.Supplier,sourceIds[0],Guid.NewGuid().ToString(),1,100)));Assert.NotEqual(JsonValueKind.Null,document.GetProperty("lines")[0].GetProperty("purchaseOrderReceiptLineId").ValueKind);}
        var details=await Task.WhenAll(sourceIds.Select(async id=>await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{id}"))));
        var payload=new{operationKey=Guid.NewGuid().ToString(),reason="Entire delivery entered incorrectly",lines=details.Select(d=>new{receiptValueId=d.GetProperty("receiptValueId").GetGuid(),expectedReceiptVersion=d.GetProperty("receiptVersion").GetUInt32(),expectedBatchVersion=d.GetProperty("batchVersion").GetUInt32()}).ToArray()};
        var result=await client.PostAsJsonAsync($"/api/purchase-order-receipts/{groupId}/reversal",payload);
        if(shipFirst||missingSource){Assert.Equal(missingSource?HttpStatusCode.NotFound:HttpStatusCode.Conflict,result.StatusCode);foreach(var id in sourceIds){var after=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{id}"));Assert.False(after.GetProperty("isVoided").GetBoolean());Assert.Empty(after.GetProperty("reversals").EnumerateArray());}}
        else{var reversed=await JsonAsync(result);Assert.Equal(2,reversed.GetProperty("lots").GetArrayLength());var after=await JsonAsync(await client.GetAsync($"/api/purchase-orders/{poId}"));Assert.Equal(10,after.GetProperty("receivedQuantity").GetInt32());Assert.Equal(0,after.GetProperty("netRetainedQuantity").GetDecimal());Assert.Equal("Received",after.GetProperty("status").GetString());}
    }

    [Fact]
    public async Task RestrictedRole_ForgedCrossTenantChildIsRejected()
    {
        var x=await SetupAsync();using var client=x.Client;var y=await SetupAsync();using var other=y.Client;
        var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));var document=await JsonAsync(await client.PostAsJsonAsync("/api/supplier-returns",Shipment(detail,x.Supplier,x.Receipt,Guid.NewGuid().ToString())));var line=document.GetProperty("lines")[0];
        Guid foreignShop;Guid ownItem;Guid ownMovement;using(var scope=_factory.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var ownReceipt=await db.PurchaseReceiptValues.SingleAsync(r=>r.Id==y.Receipt);foreignShop=ownReceipt.ShopId;ownItem=ownReceipt.ItemId;ownMovement=(await db.StockTransactions.SingleAsync(m=>m.InventoryBatchId==y.Batch)).Id;}
        await using var connection=new NpgsqlConnection(_factory.RuntimeConnectionString);await connection.OpenAsync();await using(var setting=new NpgsqlCommand("select set_config('app.active_shop_id',@shop,false)",connection)){setting.Parameters.AddWithValue("shop",foreignShop.ToString());await setting.ExecuteNonQueryAsync();}
        await using(var hidden=new NpgsqlCommand("select count(*) from supplier_returns where id=@id",connection)){hidden.Parameters.AddWithValue("id",document.GetProperty("returnId").GetGuid());Assert.Equal(0L,await hidden.ExecuteScalarAsync());}
        await using var forged=new NpgsqlCommand("""
            insert into supplier_return_lines(id,created_at,shop_id,supplier_return_id,receipt_value_id,inventory_batch_id,item_id,quantity,proposed_credit_amount,received_quantity,effective_total_cost,item_name,uqc,stock_transaction_id)
            values(@id,now(),@shop,@parent,@receipt,@batch,@item,1,0,10,1000,'Forged','NOS',@movement)
            """,connection);
        forged.Parameters.AddWithValue("id",Guid.NewGuid());forged.Parameters.AddWithValue("shop",foreignShop);forged.Parameters.AddWithValue("parent",document.GetProperty("returnId").GetGuid());forged.Parameters.AddWithValue("receipt",y.Receipt);forged.Parameters.AddWithValue("batch",y.Batch);forged.Parameters.AddWithValue("item",ownItem);forged.Parameters.AddWithValue("movement",ownMovement);
        var exception=await Assert.ThrowsAsync<PostgresException>(async()=>await forged.ExecuteNonQueryAsync());Assert.Equal(PostgresErrorCodes.ForeignKeyViolation,exception.SqlState);
    }

    private async Task<object> SalePayload(Guid batchId,decimal quantity)
    {
        using var scope=_factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var batch=await db.InventoryBatches.Include(b=>b.Item).SingleAsync(b=>b.Id==batchId);
        return new{idempotencyKey=Guid.NewGuid().ToString(),customerName="Cash customer",customerPhone=$"+91{Random.Shared.NextInt64(1000000000,9999999999)}",paymentMethod=1,paidAmount=120*quantity,dueAmount=0,items=new[]{new{barcode=batch.Item.Barcode,batchNumber=batch.BatchNumber,itemName=batch.Item.Name,quantity,costPrice=100,salesPrice=120,mrp=150,taxRatePercent=0,isPriceIncludingTax=false,inventoryBatchId=batchId}}};
    }
    [Fact]
    public async Task PartlySoldReceipt_ReturnsOnlyAvailableUnits_AndKeepsSaleCost()
    {
        var x=await SetupAsync();using var client=x.Client;var sale=await JsonAsync(await client.PostAsJsonAsync("/api/sales",await SalePayload(x.Batch,4)));
        var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));Assert.Equal(6,detail.GetProperty("returnableQuantity").GetDecimal());
        await JsonAsync(await client.PostAsJsonAsync("/api/supplier-returns",Shipment(detail,x.Supplier,x.Receipt,Guid.NewGuid().ToString())));
        var after=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));Assert.Equal(3,after.GetProperty("availableQuantity").GetDecimal());Assert.Equal(1000,after.GetProperty("netPayableAmount").GetDecimal());Assert.Equal(7,after.GetProperty("netRetainedQuantity").GetDecimal());
        using var scope=_factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var issued=await db.Sales.Include(s=>s.Items).SingleAsync(s=>s.Id==sale.GetProperty("saleId").GetGuid());Assert.Equal(100,issued.Items.Single().CostPrice);Assert.Equal(-3,await db.StockTransactions.Where(m=>m.InventoryBatchId==x.Batch&&m.TransactionType==StockTransactionType.SupplierReturn).SumAsync(m=>m.Quantity));
    }
    [Fact]
    public async Task ConsumedThenRestored_RemainsIneligibleForFullReversal()
    {
        var x=await SetupAsync();using var client=x.Client;var sale=await JsonAsync(await client.PostAsJsonAsync("/api/sales",await SalePayload(x.Batch,1)));
        Guid saleItemId;using(var scope=_factory.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();saleItemId=(await db.SaleItems.SingleAsync(si=>si.SaleId==sale.GetProperty("saleId").GetGuid())).Id;}
        await JsonAsync(await client.PostAsJsonAsync($"/api/sales/{sale.GetProperty("saleId").GetGuid()}/returns",new{payoutMethod=1,items=new[]{new{saleItemId,quantity=1,condition=(int)SaleReturnCondition.Restockable,approvedRefundAmount=120,notes="Returned in full"}}}));
        var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));Assert.Equal(10,detail.GetProperty("availableQuantity").GetDecimal());Assert.NotEmpty(detail.GetProperty("reversalEligibilityReasons").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync($"/api/purchase-receipts/{x.Receipt}/reversal",new{operationKey=Guid.NewGuid().ToString(),reason="Erroneous receipt",expectedReceiptVersion=detail.GetProperty("receiptVersion").GetUInt32(),expectedBatchVersion=detail.GetProperty("batchVersion").GetUInt32()})).StatusCode);
    }
    [Fact]
    public async Task FreeReceipt_ReversesStockWithNoZeroLedger()
    {
        var x=await SetupAsync(0);using var client=x.Client;var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));
        var result=await JsonAsync(await client.PostAsJsonAsync($"/api/purchase-receipts/{x.Receipt}/reversal",new{operationKey=Guid.NewGuid().ToString(),reason="Free receipt was erroneous",expectedReceiptVersion=detail.GetProperty("receiptVersion").GetUInt32(),expectedBatchVersion=detail.GetProperty("batchVersion").GetUInt32()}));Assert.Equal(0,result.GetProperty("lots")[0].GetProperty("effectiveAmount").GetDecimal());Assert.Equal(JsonValueKind.Null,result.GetProperty("lots")[0].GetProperty("supplierLedgerEntryId").ValueKind);
    }
    [Theory]
    [InlineData("sale")]
    [InlineData("count")]
    [InlineData("intake")]
    [InlineData("payment")]
    [InlineData("cost")]
    public async Task ShipmentVersusExistingWriter_IndependentContextsPreserveFacts(string writer)
    {
        var x=await SetupAsync();using var client=x.Client;var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));
        string path;object payload;
        if(writer=="sale"){path="/api/sales";payload=await SalePayload(x.Batch,4);}
        else if(writer=="count"){path=$"/api/inventory/batches/{x.Batch}/count";payload=new{expectedVersion=detail.GetProperty("batchVersion").GetUInt32(),countedQuantity=8,reason="Counted goods",operationKey=Guid.NewGuid().ToString()};}
        else if(writer=="payment"){path=$"/api/suppliers/{x.Supplier}/payments";payload=new{amount=1000,paymentDate=BusinessDates.Today,operationKey=Guid.NewGuid().ToString(),method="Cash",allocations=new[]{new{targetId=x.Receipt,amount=1000}}};}
        else if(writer=="cost"){path=$"/api/inventory/receipts/{x.Receipt}/cost-corrections";payload=new{expectedVersion=detail.GetProperty("receiptVersion").GetUInt32(),revisedTotalCost=1100,reason="Correct invoice",operationKey=Guid.NewGuid().ToString()};}
        else{using var scope=_factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var source=await db.InventoryBatches.Include(b=>b.Item).SingleAsync(b=>b.Id==x.Batch);path="/api/inventory/inbound";payload=new{itemName=source.Item.Name,barcode=source.Item.Barcode,uom="pcs",batchNumber=$"New-{Guid.NewGuid():N}",quantity=5,totalPurchaseCost=500,mrp=150,salesPrice=120,supplierId=x.Supplier};}
        var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Send(string url,object body){await gate.Task;return await client.PostAsJsonAsync(url,body);}
        var shipment=Send("/api/supplier-returns",Shipment(detail,x.Supplier,x.Receipt,Guid.NewGuid().ToString(),7,300,writer=="payment"));var competing=Send(path,payload);gate.SetResult();var responses=await Task.WhenAll(shipment,competing);
        foreach(var response in responses)
        {
            var body=await response.Content.ReadAsStringAsync();
            var refusedAfterShipment=writer=="sale"&&response.StatusCode==HttpStatusCode.BadRequest&&body.Contains("Sale.InsufficientStock",StringComparison.Ordinal);
            Assert.True(response.IsSuccessStatusCode||response.StatusCode==HttpStatusCode.Conflict||refusedAfterShipment,$"{writer}: {response.StatusCode} {body}");
        }Assert.Contains(responses,r=>r.IsSuccessStatusCode);
        if(writer is "sale" or "count" or "cost")Assert.Single(responses,r=>r.IsSuccessStatusCode);
        using var verifyScope=_factory.Services.CreateScope();var verify=verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var sourceReceipt=await verify.PurchaseReceiptValues.SingleAsync(r=>r.Id==x.Receipt);var aggregate=await verify.Inventory.SingleAsync(stock=>stock.ItemId==sourceReceipt.ItemId);var movementTotal=await verify.StockTransactions.Where(m=>m.ItemId==sourceReceipt.ItemId).SumAsync(m=>m.Quantity);Assert.Equal(movementTotal,aggregate.Quantity);Assert.True(aggregate.Quantity>=0);var batch=await verify.InventoryBatches.SingleAsync(b=>b.Id==x.Batch);Assert.True(batch.Quantity>=0);Assert.Equal(responses[0].IsSuccessStatusCode?1:0,await verify.SupplierReturnLines.CountAsync(l=>l.ReceiptValueId==x.Receipt));
        if(writer=="cost")Assert.Equal(responses[1].IsSuccessStatusCode?1100:1000,sourceReceipt.EffectiveTotalCost);
    }

    [Fact]
    public async Task ClosedPostingFence_RefusesShipmentAcceptanceAndReversalWithoutFacts()
    {
        var x=await SetupAsync();using var client=x.Client;var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));var document=await JsonAsync(await client.PostAsJsonAsync("/api/supplier-returns",Shipment(detail,x.Supplier,x.Receipt,Guid.NewGuid().ToString())));
        using(var scope=_factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var receipt=await db.PurchaseReceiptValues.SingleAsync(r=>r.Id==x.Receipt);var actor=(await db.ShopMemberships.SingleAsync(m=>m.ShopId==receipt.ShopId)).UserId;var period=await db.PostingPeriods.SingleAsync(p=>p.ShopId==receipt.ShopId);
            // Seed a closed fence directly; the close API's not-ended rule is covered separately.
            var closed=period.Close(actor,DateTimeOffset.UtcNow,period.EndDate.AddDays(1),"Closed fixture period");Assert.False(closed.IsError);db.PostingPeriodEvents.Add(closed.Value);await db.SaveChangesAsync();
        }
        detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));var id=document.GetProperty("returnId").GetGuid();
        var shipment=await client.PostAsJsonAsync("/api/supplier-returns",Shipment(detail,x.Supplier,x.Receipt,Guid.NewGuid().ToString(),1,100));Assert.Equal(HttpStatusCode.Conflict,shipment.StatusCode);Assert.Contains("PostingPeriod.Closed",await shipment.Content.ReadAsStringAsync(),StringComparison.Ordinal);
        var accepted=await client.PostAsJsonAsync($"/api/supplier-returns/{id}/credit",new{operationKey=Guid.NewGuid().ToString(),expectedVersion=document.GetProperty("version").GetUInt32(),reason="Agreed credit",lines=new[]{new{returnLineId=document.GetProperty("lines")[0].GetProperty("returnLineId").GetGuid(),acceptedAmount=300}}});Assert.Equal(HttpStatusCode.Conflict,accepted.StatusCode);Assert.Contains("PostingPeriod.Closed",await accepted.Content.ReadAsStringAsync(),StringComparison.Ordinal);
        var reversed=await client.PostAsJsonAsync($"/api/purchase-receipts/{x.Receipt}/reversal",new{operationKey=Guid.NewGuid().ToString(),reason="Wrong receipt",expectedReceiptVersion=detail.GetProperty("receiptVersion").GetUInt32(),expectedBatchVersion=detail.GetProperty("batchVersion").GetUInt32()});Assert.Equal(HttpStatusCode.Conflict,reversed.StatusCode);Assert.Contains("PostingPeriod.Closed",await reversed.Content.ReadAsStringAsync(),StringComparison.Ordinal);
        var after=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));Assert.Equal(7,after.GetProperty("availableQuantity").GetDecimal());Assert.Equal(1000,after.GetProperty("netPayableAmount").GetDecimal());Assert.Single(after.GetProperty("returns").EnumerateArray());Assert.Empty(after.GetProperty("reversals").EnumerateArray());
    }

    [Fact]
    public async Task OldVoid_UnresolvedSourceCannotInventPayableReversal()
    {
        var x=await SetupAsync();using var client=x.Client;
        using(var scope=_factory.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();await db.Database.ExecuteSqlAsync($"UPDATE purchase_receipt_values SET status='LEGACY_UNRESOLVED',original_total_cost=NULL,effective_total_cost=NULL WHERE id={x.Receipt}");}
        var response=await client.PostAsJsonAsync($"/api/inventory/batches/{x.Batch}/void",new{});Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);Assert.Contains("PurchaseReceipt.EvidenceMissing",await response.Content.ReadAsStringAsync(),StringComparison.Ordinal);
        var after=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));Assert.Equal(10,after.GetProperty("availableQuantity").GetDecimal());Assert.Empty(after.GetProperty("reversals").EnumerateArray());
    }
    [Fact]
    public async Task OldVoid_ResolvedReceiptDetailShowsZeroPayableWithHistoricalCost()
    {
        var x=await SetupAsync();using var client=x.Client;await JsonAsync(await client.PostAsJsonAsync($"/api/inventory/batches/{x.Batch}/void",new{}));var after=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));Assert.Equal(0,after.GetProperty("netPayableAmount").GetDecimal());Assert.Equal(0,after.GetProperty("outstandingAmount").GetDecimal());Assert.Equal(1000,after.GetProperty("effectiveTotalCost").GetDecimal());
    }

    [Fact]
    public async Task OldVoid_DamageThenVoidedAdjustmentCannotBypassSharedEligibility()
    {
        var x=await SetupAsync();using var client=x.Client;
        var adjustment=await JsonAsync(await client.PostAsJsonAsync($"/api/inventory/batches/{x.Batch}/adjust",new{direction=2,reason=1,quantity=1,notes="Damaged stock"}));
        await JsonAsync(await client.PostAsJsonAsync($"/api/inventory/adjustments/{adjustment.GetProperty("adjustmentId").GetGuid()}/void",new{reason="Adjustment entered incorrectly"}));
        var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));Assert.Equal(10,detail.GetProperty("availableQuantity").GetDecimal());
        var old=await client.PostAsJsonAsync($"/api/inventory/batches/{x.Batch}/void",new{});Assert.Equal(HttpStatusCode.Conflict,old.StatusCode);
        var modern=await client.PostAsJsonAsync($"/api/purchase-receipts/{x.Receipt}/reversal",new{operationKey=Guid.NewGuid().ToString(),reason="Wrong receipt",expectedReceiptVersion=detail.GetProperty("receiptVersion").GetUInt32(),expectedBatchVersion=detail.GetProperty("batchVersion").GetUInt32()});Assert.Equal(HttpStatusCode.Conflict,modern.StatusCode);
    }

    [Fact]
    public async Task StockInvalidation_IsAfterCommit_ReplaySafe_AndAbsentOnRollback()
    {
        var x=await SetupAsync();using var client=x.Client;var committed=new List<bool>();
        _updates.ObserveCommitted=async shop=>{await using var connection=new NpgsqlConnection(_factory.MigratorConnectionString);await connection.OpenAsync();await using var command=new NpgsqlCommand("select (select count(*) from supplier_returns where shop_id=@shop)+(select count(*) from purchase_receipt_reversals where shop_id=@shop)",connection);command.Parameters.AddWithValue("shop",shop);committed.Add((long)(await command.ExecuteScalarAsync())!>0);};
        var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));var payload=Shipment(detail,x.Supplier,x.Receipt,Guid.NewGuid().ToString());
        await JsonAsync(await client.PostAsJsonAsync("/api/supplier-returns",payload));var first=await _updates.Queue.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));Assert.Contains(detail.GetProperty("itemId").GetGuid(),first.Items);Assert.All(committed,Assert.True);
        await JsonAsync(await client.PostAsJsonAsync("/api/supplier-returns",payload));await _updates.Queue.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/supplier-returns",Shipment(detail,x.Supplier,x.Receipt,Guid.NewGuid().ToString(),1,1001,true))).StatusCode);
        var y=await SetupAsync();using var other=y.Client;var second=await JsonAsync(await other.GetAsync($"/api/purchase-receipts/{y.Receipt}"));
        await JsonAsync(await other.PostAsJsonAsync($"/api/purchase-receipts/{y.Receipt}/reversal",new{operationKey=Guid.NewGuid().ToString(),reason="Wrong receipt",expectedReceiptVersion=second.GetProperty("receiptVersion").GetUInt32(),expectedBatchVersion=second.GetProperty("batchVersion").GetUInt32()}));await _updates.Queue.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));Assert.Equal(3,_updates.Count);Assert.All(committed,Assert.True);
    }

    [Fact]
    public async Task ClosedOriginalPeriod_OpenCurrentPeriod_RefusesBothReversalsButAllowsShipment()
    {
        var original=DateTimeOffset.UtcNow.AddMonths(-1);var x=await SetupAsync(1000,original);using var client=x.Client;var month=BusinessDates.Of(original);
        await JsonAsync(await client.PostAsJsonAsync($"/api/posting-periods/{month:yyyy-MM}/close",new{operationKey=Guid.NewGuid().ToString(),reason="Close original receipt month"}));
        var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));
        var modern=await client.PostAsJsonAsync($"/api/purchase-receipts/{x.Receipt}/reversal",new{operationKey=Guid.NewGuid().ToString(),reason="Wrong source receipt",expectedReceiptVersion=detail.GetProperty("receiptVersion").GetUInt32(),expectedBatchVersion=detail.GetProperty("batchVersion").GetUInt32()});Assert.Equal(HttpStatusCode.Conflict,modern.StatusCode);Assert.Contains("PostingPeriod.Closed",await modern.Content.ReadAsStringAsync(),StringComparison.Ordinal);
        var old=await client.PostAsJsonAsync($"/api/inventory/batches/{x.Batch}/void",new{});Assert.Equal(HttpStatusCode.Conflict,old.StatusCode);Assert.Contains("PostingPeriod.Closed",await old.Content.ReadAsStringAsync(),StringComparison.Ordinal);
        await JsonAsync(await client.PostAsJsonAsync("/api/supplier-returns",Shipment(detail,x.Supplier,x.Receipt,Guid.NewGuid().ToString(),1,100)));
        var after=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));Assert.Equal(9,after.GetProperty("availableQuantity").GetDecimal());Assert.Empty(after.GetProperty("reversals").EnumerateArray());
    }
    [Fact]
    public async Task LaterCreditAcceptanceVersusPaymentAllocation_SerializesOnSharedRoots()
    {
        var x=await SetupAsync();using var client=x.Client;var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));var document=await JsonAsync(await client.PostAsJsonAsync("/api/supplier-returns",Shipment(detail,x.Supplier,x.Receipt,Guid.NewGuid().ToString())));var id=document.GetProperty("returnId").GetGuid();var paymentKey=Guid.NewGuid().ToString();
        var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Accept(){await gate.Task;return await client.PostAsJsonAsync($"/api/supplier-returns/{id}/credit",new{operationKey=Guid.NewGuid().ToString(),expectedVersion=document.GetProperty("version").GetUInt32(),reason="Agreed supplier credit",lines=new[]{new{returnLineId=document.GetProperty("lines")[0].GetProperty("returnLineId").GetGuid(),acceptedAmount=300}}});}
        async Task<HttpResponseMessage> Pay(){await gate.Task;return await client.PostAsJsonAsync($"/api/suppliers/{x.Supplier}/payments",new{amount=1000,paymentDate=BusinessDates.Today,operationKey=paymentKey,method="Cash",allocations=new[]{new{targetId=x.Receipt,amount=1000}}});}
        var accept=Accept();var payment=Pay();gate.SetResult();var responses=await Task.WhenAll(accept,payment);await JsonAsync(responses[0]);if(!responses[1].IsSuccessStatusCode)Assert.Contains("Payment.",await responses[1].Content.ReadAsStringAsync(),StringComparison.Ordinal);
        var after=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));var paid=responses[1].IsSuccessStatusCode?1000m:0m;Assert.Equal(7,after.GetProperty("availableQuantity").GetDecimal());Assert.Equal(700,after.GetProperty("netPayableAmount").GetDecimal());Assert.Equal(paid,after.GetProperty("allocatedPaymentAmount").GetDecimal());Assert.Equal(Math.Max(0,paid-700),after.GetProperty("supplierCreditAmount").GetDecimal());Assert.Equal(Math.Max(0,700-paid),after.GetProperty("outstandingAmount").GetDecimal());
        using var scope=_factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();Assert.Equal(1,await db.SupplierReturnCredits.CountAsync(c=>c.SupplierReturnId==id));Assert.Equal(responses[1].IsSuccessStatusCode?1:0,await db.OperationReceipts.CountAsync(o=>o.OperationKey==paymentKey));
    }

    [Theory]
    [InlineData(0.29)]
    [InlineData(1.10)]
    [InlineData(2.30)]
    public async Task LaterAcceptance_RecordsCanonicalPaiseExactly(decimal amount)
    {
        var x=await SetupAsync();using var client=x.Client;var detail=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));var document=await JsonAsync(await client.PostAsJsonAsync("/api/supplier-returns",Shipment(detail,x.Supplier,x.Receipt,Guid.NewGuid().ToString())));var id=document.GetProperty("returnId").GetGuid();
        var accepted=await JsonAsync(await client.PostAsJsonAsync($"/api/supplier-returns/{id}/credit",new{operationKey=Guid.NewGuid().ToString(),expectedVersion=document.GetProperty("version").GetUInt32(),reason="Agreed paise credit",lines=new[]{new{returnLineId=document.GetProperty("lines")[0].GetProperty("returnLineId").GetGuid(),acceptedAmount=amount}}}));Assert.Equal(amount,accepted.GetProperty("lines")[0].GetProperty("acceptedAmount").GetDecimal());
        var after=await JsonAsync(await client.GetAsync($"/api/purchase-receipts/{x.Receipt}"));Assert.Equal(1000-amount,after.GetProperty("netPayableAmount").GetDecimal());
    }
}
