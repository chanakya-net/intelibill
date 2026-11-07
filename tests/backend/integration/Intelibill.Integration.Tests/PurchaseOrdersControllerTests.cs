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
public sealed class PurchaseOrdersControllerTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login/email", new
        {
            email,
            password,
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static async Task<Guid> GetShopIdFromTokenAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/shops/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.EnumerateArray().First().GetProperty("shopId").GetGuid();
    }

    private static async Task<(string Email, string Password)> AddUserAsync(HttpClient client, string ownerToken, Guid shopId, string role)
    {
        var userEmail = UniqueEmail();
        var userPassword = "Pass123!Aa";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            shopIds = new[] { shopId },
            email = userEmail,
            firstName = "Shop",
            lastName = role,
            phoneNumber = UniquePhone(),
            password = userPassword,
            confirmPassword = userPassword,
            role,
        });
        var response = await client.SeedVerifiedStaffAsync(request);
        response.EnsureSuccessStatusCode();

        return (userEmail, userPassword);
    }

    private static async Task<(string Email, string Password)> AddStaffAsync(HttpClient client, string ownerToken, Guid shopId) =>
        await AddUserAsync(client, ownerToken, shopId, "Staff");

    private static async Task<(string Email, string Password)> AddManagerAsync(HttpClient client, string ownerToken, Guid shopId) =>
        await AddUserAsync(client, ownerToken, shopId, "Manager");

    private static async Task<Guid> CreateItemAsync(HttpClient client, string token, string name)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name,
            barcode = $"ITM-{Guid.NewGuid():N}",
            uom = "pcs",
            isActive = true,
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> CreateDraftAsync(
        HttpClient client,
        string token,
        string prefix,
        string? supplierName = null,
        string? supplierReference = null)
    {
        var itemAId = await CreateItemAsync(client, token, $"Item A {prefix}");
        var itemBId = await CreateItemAsync(client, token, $"Item B {prefix}");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/purchase-orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            notes = $"Draft for {prefix}",
            supplierName,
            supplierReference,
            lines = new[]
            {
                new { itemId = itemAId, description = "Item A", expectedQuantity = 3, unitCost = 100m },
                new { itemId = itemBId, description = "Item B", expectedQuantity = 2, unitCost = 25m },
            },
        });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task CreatePurchaseOrderDraft_AsOwner_ReturnsGeneratedPoNumberAndDraftStatus()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var body = await CreateDraftAsync(client, ownerToken, "Owner");

        Assert.Equal("Draft", body.GetProperty("status").GetString());
        Assert.Matches(@"^PO-\d{4}-\d{6}$", body.GetProperty("purchaseOrderNumber").GetString()!);
        Assert.Equal("Draft for Owner", body.GetProperty("notes").GetString());
        Assert.Equal(350m, body.GetProperty("expectedTotal").GetDecimal());
        Assert.Equal(2, body.GetProperty("lines").GetArrayLength());
        Assert.Equal("Item A", body.GetProperty("lines")[0].GetProperty("description").GetString());
    }

    [Fact]
    public async Task CreatePurchaseOrderDraft_PersistsSupplierMetadata()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var body = await CreateDraftAsync(client, ownerToken, "Owner", "Acme Traders", "SUP-REF-001");

        var purchaseOrderId = body.GetProperty("purchaseOrderId").GetGuid();
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/purchase-orders?search={Uri.EscapeDataString("Acme")}");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var listResponse = await client.SendAsync(listRequest);
        listResponse.EnsureSuccessStatusCode();
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, listBody.GetProperty("totalCount").GetInt32());
        var item = Assert.Single(listBody.GetProperty("items").EnumerateArray());
        Assert.Equal(purchaseOrderId, item.GetProperty("purchaseOrderId").GetGuid());
        Assert.Equal("Acme Traders", item.GetProperty("supplierName").GetString());
        Assert.Equal("SUP-REF-001", item.GetProperty("supplierReference").GetString());
    }

    [Fact]
    public async Task CreatePurchaseOrderDraft_AsStaff_ReturnsForbidden()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var itemId = await CreateItemAsync(client, ownerToken, "Staff Attempt Item");

        var ownerShopId = await GetShopIdFromTokenAsync(client, ownerToken);
        var (staffEmail, staffPassword) = await AddStaffAsync(client, ownerToken, ownerShopId);
        var staffToken = await LoginAsync(client, staffEmail, staffPassword);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/purchase-orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        request.Content = JsonContent.Create(new
        {
            notes = "Staff attempt",
            lines = new[] { new { itemId, description = "Item", expectedQuantity = 1, unitCost = 10m } },
        });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListPurchaseOrders_OnlyReturnsActiveShopData()
    {
        using var client = CreateClient();
        var ownerTokenA = await CreateShopAsync(client, await RegisterAsync(client));
        var draftA = await CreateDraftAsync(client, ownerTokenA, "ShopA");
        var poAId = draftA.GetProperty("purchaseOrderId").GetGuid();

        var ownerTokenB = await CreateShopAsync(client, await RegisterAsync(client));
        var draftB = await CreateDraftAsync(client, ownerTokenB, "ShopB");
        var poBId = draftB.GetProperty("purchaseOrderId").GetGuid();

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/purchase-orders");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenA);
        var listResponse = await client.SendAsync(listRequest);
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();

        var ids = list.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("purchaseOrderId").GetGuid())
            .ToArray();
        Assert.Contains(poAId, ids);
        Assert.DoesNotContain(poBId, ids);
    }

    [Fact]
    public async Task ListPurchaseOrders_SupportsSearchStatusDateAndNormalizedPagination()
    {
        using var client = CreateClient();
        var ownerToken = await CreateShopAsync(client, await RegisterAsync(client));

        var alphaDraft = await CreateDraftAsync(client, ownerToken, "Alpha Rice");
        await CreateDraftAsync(client, ownerToken, "Beta Oil");

        var createdAt = alphaDraft.GetProperty("createdAt").GetDateTimeOffset();
        var orderDate = BusinessDates.Of(createdAt);
        var poNumber = alphaDraft.GetProperty("purchaseOrderNumber").GetString()!;

        using var poNumberRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/purchase-orders?search={Uri.EscapeDataString(poNumber)}&status=Draft&page=0&page_size=999");
        poNumberRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var poNumberResponse = await client.SendAsync(poNumberRequest);
        poNumberResponse.EnsureSuccessStatusCode();
        var poNumberBody = await poNumberResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, poNumberBody.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, poNumberBody.GetProperty("pageNumber").GetInt32());
        Assert.Equal(100, poNumberBody.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, poNumberBody.GetProperty("items").GetArrayLength());

        using var lineSearchRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/purchase-orders?search={Uri.EscapeDataString("Item A")}&order_date_from={orderDate:yyyy-MM-dd}&order_date_to={orderDate:yyyy-MM-dd}");
        lineSearchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var lineSearchResponse = await client.SendAsync(lineSearchRequest);
        lineSearchResponse.EnsureSuccessStatusCode();
        var lineSearchBody = await lineSearchResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, lineSearchBody.GetProperty("totalCount").GetInt32());
        Assert.Equal(20, lineSearchBody.GetProperty("pageSize").GetInt32());
        Assert.All(
            lineSearchBody.GetProperty("items").EnumerateArray(),
            item => Assert.Equal("Draft", item.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task ListPurchaseOrders_SearchesSupplierFieldsCreatedViaApiFlow()
    {
        using var client = CreateClient();
        var ownerToken = await CreateShopAsync(client, await RegisterAsync(client));

        var matchingDraft = await CreateDraftAsync(
            client,
            ownerToken,
            "Alpha Rice",
            "Acme Traders",
            "SUP-REF-001");
        var matchingId = matchingDraft.GetProperty("purchaseOrderId").GetGuid();
        var nonMatchingDraft = await CreateDraftAsync(
            client,
            ownerToken,
            "Beta Oil",
            "Bravo Foods",
            "SUP-REF-999");
        var nonMatchingId = nonMatchingDraft.GetProperty("purchaseOrderId").GetGuid();

        using var supplierNameRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/purchase-orders?search={Uri.EscapeDataString("Acme")}");
        supplierNameRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var supplierNameResponse = await client.SendAsync(supplierNameRequest);
        supplierNameResponse.EnsureSuccessStatusCode();
        var supplierNameBody = await supplierNameResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, supplierNameBody.GetProperty("totalCount").GetInt32());
        var supplierNameItem = Assert.Single(supplierNameBody.GetProperty("items").EnumerateArray());
        Assert.Equal(matchingId, supplierNameItem.GetProperty("purchaseOrderId").GetGuid());
        Assert.Equal("Acme Traders", supplierNameItem.GetProperty("supplierName").GetString());
        Assert.Equal("SUP-REF-001", supplierNameItem.GetProperty("supplierReference").GetString());
        Assert.Equal(0, supplierNameItem.GetProperty("receivedQuantity").GetInt32());

        using var supplierReferenceRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/purchase-orders?search={Uri.EscapeDataString("SUP-REF-001")}");
        supplierReferenceRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var supplierReferenceResponse = await client.SendAsync(supplierReferenceRequest);
        supplierReferenceResponse.EnsureSuccessStatusCode();
        var supplierReferenceBody = await supplierReferenceResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, supplierReferenceBody.GetProperty("totalCount").GetInt32());
        var supplierReferenceItem = Assert.Single(supplierReferenceBody.GetProperty("items").EnumerateArray());
        Assert.Equal(matchingId, supplierReferenceItem.GetProperty("purchaseOrderId").GetGuid());
        Assert.DoesNotContain(
            supplierReferenceBody.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("purchaseOrderId").GetGuid()),
            id => id == nonMatchingId);
    }

    [Fact]
    public async Task GetPurchaseOrderDetail_ForOtherShopReturnsNotFound()
    {
        using var client = CreateClient();
        var ownerTokenA = await CreateShopAsync(client, await RegisterAsync(client));
        var draftA = await CreateDraftAsync(client, ownerTokenA, "ShopA");
        var poAId = draftA.GetProperty("purchaseOrderId").GetGuid();

        var ownerTokenB = await CreateShopAsync(client, await RegisterAsync(client));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/purchase-orders/{poAId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PurchaseOrderMutations_ForOtherActiveShopReturnNotFoundAndDoNotMutateSourceShopData()
    {
        using var client = CreateClient();
        var ownerTokenA = await CreateShopAsync(client, await RegisterAsync(client));
        var supplierIdA = await CreateSupplierAsync(client, ownerTokenA, "Isolation Supplier A");
        var draftForUpdate = await CreateDraftWithSupplierAsync(client, ownerTokenA, supplierIdA, "IsoUpdate");
        var draftForDelete = await CreateDraftWithSupplierAsync(client, ownerTokenA, supplierIdA, "IsoDelete");
        var placedForCancel = await CreatePlacedSingleLinePoAsync(client, ownerTokenA, supplierIdA, "IsoCancel");
        var placedForReceive = await CreatePlacedSingleLinePoAsync(client, ownerTokenA, supplierIdA, "IsoReceive");
        var partialForClose = await CreatePlacedSingleLinePoAsync(client, ownerTokenA, supplierIdA, "IsoClose", expectedQuantity: 2);

        using (var receiveRequest = CreateReceiveRequest(ownerTokenA, partialForClose.PurchaseOrderId, partialForClose.LineId, $"B-{Guid.NewGuid():N}", quantity: 1))
        {
            (await client.SendAsync(receiveRequest)).EnsureSuccessStatusCode();
        }

        var ownerTokenB = await CreateShopAsync(client, await RegisterAsync(client));
        var otherShopItemId = await CreateItemAsync(client, ownerTokenB, "Other shop replacement item");

        var updatePoId = draftForUpdate.GetProperty("purchaseOrderId").GetGuid();
        using var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/purchase-orders/{updatePoId}");
        updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);
        updateRequest.Content = JsonContent.Create(new
        {
            lines = new[] { new { itemId = otherShopItemId, description = "Other shop item", expectedQuantity = 1, unitCost = 1m } },
        });

        using var placeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{draftForUpdate.GetProperty("purchaseOrderId").GetGuid()}/place");
        placeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/purchase-orders/{draftForDelete.GetProperty("purchaseOrderId").GetGuid()}");
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);

        using var cancelRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{placedForCancel.PurchaseOrderId}/cancel");
        cancelRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);
        cancelRequest.Content = JsonContent.Create(new { reason = "Other shop cancel" });

        using var closeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{partialForClose.PurchaseOrderId}/close");
        closeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);
        closeRequest.Content = JsonContent.Create(new { reason = "Other shop close" });

        using var receiveRequestB = CreateReceiveRequest(ownerTokenB, placedForReceive.PurchaseOrderId, placedForReceive.LineId, $"B-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(updateRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(placeRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(deleteRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(cancelRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(receiveRequestB)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(closeRequest)).StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(PurchaseOrderStatus.Draft, (await db.PurchaseOrders.SingleAsync(p => p.Id == updatePoId)).Status);
        Assert.Equal(PurchaseOrderStatus.Draft, (await db.PurchaseOrders.SingleAsync(p => p.Id == draftForDelete.GetProperty("purchaseOrderId").GetGuid())).Status);
        Assert.Equal(PurchaseOrderStatus.Placed, (await db.PurchaseOrders.SingleAsync(p => p.Id == placedForCancel.PurchaseOrderId)).Status);
        Assert.Equal(PurchaseOrderStatus.Placed, (await db.PurchaseOrders.SingleAsync(p => p.Id == placedForReceive.PurchaseOrderId)).Status);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, (await db.PurchaseOrders.SingleAsync(p => p.Id == partialForClose.PurchaseOrderId)).Status);
        Assert.Equal(1, await db.PurchaseOrderReceipts.CountAsync(r => r.PurchaseOrderId == partialForClose.PurchaseOrderId));
    }

    [Fact]
    public async Task CreatePurchaseOrderDraft_GeneratesSequencePerShopAndYear()
    {
        using var client = CreateClient();
        var ownerToken = await CreateShopAsync(client, await RegisterAsync(client));

        var first = await CreateDraftAsync(client, ownerToken, "First");
        var second = await CreateDraftAsync(client, ownerToken, "Second");

        var firstNumber = first.GetProperty("purchaseOrderNumber").GetString()!;
        var secondNumber = second.GetProperty("purchaseOrderNumber").GetString()!;

        Assert.NotEqual(firstNumber, secondNumber);
        Assert.Matches(@"^PO-\d{4}-000001$", firstNumber);
        Assert.Matches(@"^PO-\d{4}-\d{6}$", firstNumber);
        Assert.Matches(@"^PO-\d{4}-000002$", secondNumber);
    }

    [Fact]
    public async Task CreatePurchaseOrderDraft_ConcurrentCreates_ReturnsUniquePoNumbersWithoutFailures()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        // Run 5 concurrent creates
        var tasks = Enumerable.Range(1, 5).Select(i => Task.Run(async () =>
        {
            using var localClient = CreateClient();
            return await CreateDraftAsync(localClient, ownerToken, $"Concurrent-{i}");
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        var poNumbers = results.Select(r => r.GetProperty("purchaseOrderNumber").GetString()!).ToArray();

        // Assert all PO numbers are unique
        Assert.Equal(5, poNumbers.Distinct().Count());

        // Assert they follow the expected pattern
        foreach (var num in poNumbers)
        {
            Assert.Matches(@"^PO-\d{4}-\d{6}$", num);
        }
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

    private static async Task<JsonElement> CreateDraftWithSupplierAsync(
        HttpClient client,
        string token,
        Guid supplierId,
        string prefix)
    {
        var itemId = await CreateItemAsync(client, token, $"Item {prefix}");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/purchase-orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            supplierId,
            notes = $"Draft for {prefix}",
            lines = new[]
            {
                new { itemId, description = "Item A", expectedQuantity = 3, unitCost = 100m },
            },
        });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<(Guid PurchaseOrderId, Guid LineId)> CreatePlacedSingleLinePoAsync(
        HttpClient client,
        string token,
        Guid supplierId,
        string prefix,
        int expectedQuantity = 1)
    {
        var itemId = await CreateItemAsync(client, token, $"Receive Item {prefix}");

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/purchase-orders");
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createRequest.Content = JsonContent.Create(new
        {
            supplierId,
            notes = $"Receive draft {prefix}",
            lines = new[]
            {
                new { itemId, description = "Receive Item", expectedQuantity, unitCost = 10m },
            },
        });

        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var draft = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var poId = draft.GetProperty("purchaseOrderId").GetGuid();
        var lineId = draft.GetProperty("lines")[0].GetProperty("lineId").GetGuid();

        using var placeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{poId}/place");
        placeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await client.SendAsync(placeRequest)).EnsureSuccessStatusCode();

        return (poId, lineId);
    }

    private static async Task<(Guid PurchaseOrderId, Guid FirstLineId, Guid SecondLineId)> CreatePlacedTwoLinePoAsync(
        HttpClient client,
        string token,
        Guid supplierId,
        string prefix,
        int expectedQuantity = 1)
    {
        var firstItemId = await CreateItemAsync(client, token, $"Receive Item A {prefix}");
        var secondItemId = await CreateItemAsync(client, token, $"Receive Item B {prefix}");

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/purchase-orders");
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createRequest.Content = JsonContent.Create(new
        {
            supplierId,
            notes = $"Receive draft {prefix}",
            lines = new[]
            {
                new { itemId = firstItemId, description = "Receive Item A", expectedQuantity, unitCost = 10m },
                new { itemId = secondItemId, description = "Receive Item B", expectedQuantity, unitCost = 20m },
            },
        });

        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var draft = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var poId = draft.GetProperty("purchaseOrderId").GetGuid();
        var firstLineId = draft.GetProperty("lines")[0].GetProperty("lineId").GetGuid();
        var secondLineId = draft.GetProperty("lines")[1].GetProperty("lineId").GetGuid();

        using var placeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{poId}/place");
        placeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await client.SendAsync(placeRequest)).EnsureSuccessStatusCode();

        return (poId, firstLineId, secondLineId);
    }

    private static HttpRequestMessage CreateReceiveRequest(
        string token,
        Guid purchaseOrderId,
        Guid lineId,
        string batchNumber,
        int quantity = 1)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{purchaseOrderId}/receipts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            referenceNumber = $"REF-{batchNumber}",
            notes = "Received via test",
            lines = new[]
            {
                new
                {
                    purchaseOrderLineId = lineId,
                    barcode = $"IT-{lineId:N}",
                    batchNumber,
                    quantity,
                    totalPurchaseCost = 10m,
                    mrp = 12m,
                    salesPrice = 11m,
                    taxRatePercent = 5m,
                    taxIncluded = false,
                    purchaseTaxIncluded = false,
                    expiryDate = (DateOnly?)null,
                    manufacturingDate = (DateOnly?)null,
                },
            },
        });

        return request;
    }

    private static ReceivePurchaseOrderLineRequest Lot(Guid lineId, string batch, int quantity, decimal cost) =>
        new(lineId, "REPEATED-PRODUCT", batch, quantity, cost, 30, 20, 5, false, true, null, null, Guid.NewGuid());

    private static async Task<HttpResponseMessage> PostLotsAsync(HttpClient client, string token, Guid poId,
        IReadOnlyList<ReceivePurchaseOrderLineRequest> lines, Guid? operationId = null, DateTimeOffset? receivedAt = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{poId}/receipts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new ReceivePurchaseOrderRequest("REF-LOTS", "Lots", receivedAt, lines, operationId));
        var response = await client.SendAsync(request);
        return response;
    }

    private async Task AssertPostingAsync(Guid poId, Guid supplierId, int receiptCount, int lotCount, decimal quantity, decimal value, int operations)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(receiptCount, await db.PurchaseOrderReceipts.CountAsync(r => r.PurchaseOrderId == poId));
        Assert.Equal(lotCount, await db.PurchaseOrderReceiptLines.CountAsync());
        Assert.Equal(lotCount, await db.InventoryBatches.CountAsync());
        Assert.Equal(lotCount, await db.StockTransactions.CountAsync());
        Assert.Equal(quantity, await db.StockTransactions.SumAsync(t => t.Quantity));
        Assert.Equal(quantity, await db.Inventory.SumAsync(i => i.Quantity));
        Assert.Equal(lotCount, await db.PurchaseReceiptValues.CountAsync());
        Assert.Equal(value, await db.PurchaseReceiptValues.SumAsync(v => v.OriginalTotalCost));
        Assert.Equal(value, await db.SupplierLedgerEntries.Where(e => e.SupplierId == supplierId).SumAsync(e => e.Amount));
        Assert.Equal(operations, await db.OperationReceipts.CountAsync(r => r.OperationType == FinancialOperationType.PurchaseOrderReceipt));
        Assert.Equal(quantity, await db.PurchaseOrderLines.Where(l => l.PurchaseOrderId == poId).SumAsync(l => l.ReceivedQuantity));
    }

    [Fact]
    public async Task SamePoLine_TwoLots_ReceivesGroupedTotal()
    {
        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));
        var supplier = await CreateSupplierAsync(client, token, "Multi lots");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, token, supplier, "Lots", 10);
        var lots = new[] { Lot(lineId, "A", 4, 40), Lot(lineId, "B", 6, 90) };
        var response = await PostLotsAsync(client, token, poId, lots, Guid.NewGuid());
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var receipt = Assert.Single(body.GetProperty("receipts").EnumerateArray());
        Assert.Equal(2, receipt.GetProperty("lines").GetArrayLength());
        Assert.Equal(lots.Select(l => l.ClientLineId).Order(), receipt.GetProperty("lines").EnumerateArray().Select(l => (Guid?)l.GetProperty("clientLineId").GetGuid()).Order());
        await AssertPostingAsync(poId, supplier, 1, 2, 10, 130, 1);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var lines = await db.PurchaseOrderReceiptLines.ToListAsync();
        Assert.All(lines, l => { Assert.False(l.TaxIncluded); Assert.True(l.PurchaseTaxIncluded); });
        foreach (var line in lines)
        {
            var value = await db.PurchaseReceiptValues.SingleAsync(v => v.PurchaseOrderReceiptLineId == line.Id);
            Assert.Equal(line.InventoryBatchId, value.InventoryBatchId);
            Assert.Equal(line.Quantity, value.ReceivedQuantity);
            Assert.Equal(line.TotalPurchaseCost, value.OriginalTotalCost);
        }
    }

    [Fact]
    public async Task ReceivedLots_AfterSaleAndCostCorrection_PreserveOriginalLineage()
    {
        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));
        var shopId = await GetShopIdFromTokenAsync(client, token);
        using (var profileRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/shops/{shopId}/tax-profile"))
        {
            profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            profileRequest.Content = JsonContent.Create(new { stateCode = "29", taxRegime = "Regular", eInvoiceStatus = "BelowThreshold", gstNumber = TestTaxProfile.NewGstin() });
            var profileResponse = await client.SendAsync(profileRequest);
            Assert.True(profileResponse.IsSuccessStatusCode, await profileResponse.Content.ReadAsStringAsync());
        }
        var supplier = await CreateSupplierAsync(client, token, "Lineage lots");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, token, supplier, "Lineage", 10);
        var lots = new[] { Lot(lineId, "A", 4, 40), Lot(lineId, "B", 6, 90) };
        var receiveResponse = await PostLotsAsync(client, token, poId, lots, Guid.NewGuid());
        Assert.True(receiveResponse.IsSuccessStatusCode, await receiveResponse.Content.ReadAsStringAsync());
        var original = await receiveResponse.Content.ReadFromJsonAsync<JsonElement>();
        var originalLines = original.GetProperty("receipts")[0].GetProperty("lines").Clone();
        var originalA = originalLines.EnumerateArray().Single(l => l.GetProperty("clientLineId").GetGuid() == lots[0].ClientLineId);
        var batchA = originalA.GetProperty("inventoryBatchId").GetGuid();
        Guid valueId;
        uint version;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var value = await db.PurchaseReceiptValues.SingleAsync(v => v.InventoryBatchId == batchA);
            valueId = value.Id;
            version = value.Version;
        }
        using (var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales"))
        {
            saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            saleRequest.Content = JsonContent.Create(new
            {
                idempotencyKey = $"lot-sale-{Guid.NewGuid():N}", customerId = (Guid?)null,
                customerName = "Walk-in", paymentMethod = (int)PaymentMethod.Cash, paidAmount = 42m, dueAmount = 0m,
                items = new[] { new { barcode = lots[0].Barcode, batchNumber = "A", itemName = "Lineage",
                    quantity = 2m, costPrice = 10m, salesPrice = 20m, mrp = 30m,
                    taxRatePercent = 5m, isPriceIncludingTax = false, inventoryBatchId = batchA } },
            });
            var saleResponse = await client.SendAsync(saleRequest);
            Assert.True(saleResponse.IsSuccessStatusCode, await saleResponse.Content.ReadAsStringAsync());
        }
        using (var correctionRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/receipts/{valueId}/cost-corrections"))
        {
            correctionRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            correctionRequest.Content = JsonContent.Create(new { expectedVersion = version, revisedTotalCost = 44m,
                reason = "Supplier invoice correction", operationKey = $"lot-correction-{Guid.NewGuid():N}" });
            var correctionResponse = await client.SendAsync(correctionRequest);
            Assert.True(correctionResponse.IsSuccessStatusCode, await correctionResponse.Content.ReadAsStringAsync());
        }
        using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/purchase-orders/{poId}");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var detailResponse = await client.SendAsync(detailRequest);
        detailResponse.EnsureSuccessStatusCode();
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        // Whole immutable history rows include receipt/client/PO/item/batch/movement IDs and all original amounts.
        var currentLines = detail.GetProperty("receipts")[0].GetProperty("lines");
        Assert.Equal(originalLines.GetArrayLength(), currentLines.GetArrayLength());
        foreach (var before in originalLines.EnumerateArray())
        {
            var after = currentLines.EnumerateArray().Single(l => l.GetProperty("receiptLineId").GetGuid() == before.GetProperty("receiptLineId").GetGuid());
            foreach (var property in before.EnumerateObject())
            {
                var current = after.GetProperty(property.Name);
                if (property.Value.ValueKind == JsonValueKind.Number)
                    Assert.Equal(property.Value.GetDecimal(), current.GetDecimal());
                else
                    Assert.Equal(property.Value.GetRawText(), current.GetRawText());
            }
        }
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = await verifyDb.PurchaseOrderReceiptLines.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        foreach (var lot in lots)
        {
            var row = rows.Single(r => r.ClientLineId == lot.ClientLineId);
            Assert.Equal(lineId, row.PurchaseOrderLineId);
            Assert.Equal(lot.Quantity, row.Quantity);
            Assert.Equal(lot.TotalPurchaseCost, row.TotalPurchaseCost);
            var stock = await verifyDb.StockTransactions.SingleAsync(t => t.Id == row.StockTransactionId);
            Assert.Equal(row.InventoryBatchId, stock.InventoryBatchId);
            Assert.Equal(row.Quantity, stock.Quantity);
            var batch = await verifyDb.InventoryBatches.SingleAsync(b => b.Id == row.InventoryBatchId);
            Assert.Equal(lot.Quantity, batch.OriginalQuantity);
            Assert.Equal(lot.BatchNumber == "A" ? 2m : 6m, batch.Quantity);
            var value = await verifyDb.PurchaseReceiptValues.SingleAsync(v => v.PurchaseOrderReceiptLineId == row.Id);
            Assert.Equal(row.InventoryBatchId, value.InventoryBatchId);
            Assert.Equal(row.Quantity, value.ReceivedQuantity);
            Assert.Equal(lot.TotalPurchaseCost, value.OriginalTotalCost);
            Assert.Equal(lot.BatchNumber == "A" ? 44m : 90m, value.EffectiveTotalCost);
        }
        var adjustment = Assert.Single(await verifyDb.PurchaseReceiptCostAdjustments.ToListAsync());
        Assert.Equal(valueId, adjustment.PurchaseReceiptValueId);
        Assert.Equal(4m, adjustment.Delta);
        Assert.Equal(1, await verifyDb.SupplierLedgerEntries.CountAsync(e => e.EntryType == SupplierLedgerEntryType.CostAdjusted));
        Assert.Equal(1, await verifyDb.Sales.CountAsync());
    }

    [Fact]
    public async Task GroupedOverRemaining_RejectsAllRowsWithoutWrites()
    {
        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));
        var supplier = await CreateSupplierAsync(client, token, "Over lots");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, token, supplier, "Over lots", 10);
        var lots = new[] { Lot(lineId, "A", 6, 40), Lot(lineId, "B", 6, 90) };
        var response = await PostLotsAsync(client, token, poId, lots, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var rows = problem.GetProperty("rowErrors").EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.All(rows, row => { Assert.Equal("quantity", row.GetProperty("field").GetString()); Assert.Equal("PurchaseOrder.ReceiptQuantityOverRemaining", row.GetProperty("code").GetString()); });
        Assert.Equal(lots.Select(l => l.ClientLineId), rows.Select(r => (Guid?)r.GetProperty("clientLineId").GetGuid()));
        Assert.Equal(0, rows[0].GetProperty("lineIndex").GetInt32());
        Assert.Equal(1, rows[1].GetProperty("lineIndex").GetInt32());
        await AssertPostingAsync(poId, supplier, 0, 0, 0, 0, 0);
    }

    [Fact]
    public async Task SecondLotProcessorFailure_RollsBackEverything()
    {
        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));
        var supplier = await CreateSupplierAsync(client, token, "Rollback lots");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, token, supplier, "Rollback lots", 11);
        // Existing B forces a failure inside the second processor invocation, after A has staged its stock/payable.
        (await PostLotsAsync(client, token, poId, [Lot(lineId, "B", 1, 10)])).EnsureSuccessStatusCode();
        var second = Lot(lineId, "B", 6, 90);
        var response = await PostLotsAsync(client, token, poId, [Lot(lineId, "A", 4, 40), second], Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row = Assert.Single(problem.GetProperty("rowErrors").EnumerateArray());
        Assert.Equal(second.ClientLineId, row.GetProperty("clientLineId").GetGuid());
        Assert.Equal(1, row.GetProperty("lineIndex").GetInt32());
        Assert.Equal("batchNumber", row.GetProperty("field").GetString());
        await AssertPostingAsync(poId, supplier, 1, 1, 1, 10, 0);
    }

    [Fact]
    public async Task SameKeyConcurrent_PostsOnce()
    {
        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));
        var supplier = await CreateSupplierAsync(client, token, "Same key");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, token, supplier, "Same key", 10);
        var lots = new[] { Lot(lineId, "A", 4, 40), Lot(lineId, "B", 6, 90) };
        var operation = Guid.NewGuid();
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            using var parallelClient = CreateClient();
            var response = await PostLotsAsync(parallelClient, token, poId, lots, operation);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }));
        Assert.All(responses, response => Assert.True(JsonElement.DeepEquals(responses[0], response)));
        await AssertPostingAsync(poId, supplier, 1, 2, 10, 130, 1);
    }

    [Fact]
    public async Task DifferentKeys_FinalQuantity_OnlyOnePosts()
    {
        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));
        var supplier = await CreateSupplierAsync(client, token, "Different keys");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, token, supplier, "Different keys", 10);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i =>
        {
            using var parallelClient = CreateClient();
            return await PostLotsAsync(parallelClient, token, poId, [Lot(lineId, $"A-{i}", 10, 130)], Guid.NewGuid());
        }));
        Assert.Single(responses, r => r.IsSuccessStatusCode);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
        await AssertPostingAsync(poId, supplier, 1, 1, 10, 130, 1);
    }

    [Fact]
    public async Task LostResponse_ReplayAndLookup_AfterPoAndPeriodClose()
    {
        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));
        var supplier = await CreateSupplierAsync(client, token, "Recovery");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, token, supplier, "Recovery", 11);
        var lots = new[] { Lot(lineId, "A", 4, 40), Lot(lineId, "B", 6, 90) };
        var operation = Guid.NewGuid();
        var today = BusinessDates.Today;
        var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var at = new DateTimeOffset(month.Year, month.Month, 2, 12, 0, 0, TimeSpan.Zero);
        var posted = await PostLotsAsync(client, token, poId, lots, operation, at);
        posted.EnsureSuccessStatusCode();
        var original = await posted.Content.ReadFromJsonAsync<JsonElement>();
        using (var close = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{poId}/close"))
        {
            close.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            close.Content = JsonContent.Create(new { reason = "Finished receiving" });
            (await client.SendAsync(close)).EnsureSuccessStatusCode();
        }
        using (var close = new HttpRequestMessage(HttpMethod.Post, $"/api/posting-periods/{month:yyyy-MM}/close"))
        {
            close.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            close.Content = JsonContent.Create(new { reason = "Month finished", operationKey = Guid.NewGuid().ToString() });
            (await client.SendAsync(close)).EnsureSuccessStatusCode();
        }
        var replay = await PostLotsAsync(client, token, poId, lots, operation, at);
        replay.EnsureSuccessStatusCode();
        Assert.True(JsonElement.DeepEquals(original, await replay.Content.ReadFromJsonAsync<JsonElement>()));
        using var lookupRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/financial-operations/purchase-order-receipt/{operation:D}");
        lookupRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var lookup = await client.SendAsync(lookupRequest);
        lookup.EnsureSuccessStatusCode();
        var found = await lookup.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(poId, found.GetProperty("targetId").GetGuid());
        Assert.Equal(operation.ToString("D"), found.GetProperty("operationKey").GetString());
        Assert.True(JsonElement.DeepEquals(original, found.GetProperty("response")));
        await AssertPostingAsync(poId, supplier, 1, 2, 10, 130, 1);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.OperationReceipts.SingleAsync(r => r.OperationType == FinancialOperationType.PurchaseOrderReceipt);
        using var payload = JsonDocument.Parse(stored.ResponseBody!);
        Assert.True(JsonElement.DeepEquals(original, payload.RootElement));
        Assert.False(payload.RootElement.TryGetProperty("value", out _));
    }

    [Fact]
    public async Task SameKeyChangedPayload_Conflicts()
    {
        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));
        var supplier = await CreateSupplierAsync(client, token, "Mismatch");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, token, supplier, "Mismatch", 10);
        var lot = Lot(lineId, "A", 10, 130);
        var operation = Guid.NewGuid();
        (await PostLotsAsync(client, token, poId, [lot], operation)).EnsureSuccessStatusCode();
        var changed = await PostLotsAsync(client, token, poId, [lot with { TotalPurchaseCost = 131 }], operation);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        var problem = await changed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Operation.PayloadMismatch", problem.GetProperty("title").GetString());
        await AssertPostingAsync(poId, supplier, 1, 1, 10, 130, 1);
    }

    [Theory]
    [InlineData("duplicate-lot")]
    [InlineData("duplicate-client")]
    [InlineData("foreign-line")]
    public async Task InvalidLots_RejectWithRowIdentityAndNoEffects(string scenario)
    {
        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));
        var supplier = await CreateSupplierAsync(client, token, "Invalid lots");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, token, supplier, "Invalid lots", 10);
        var a = Lot(lineId, "A", 4, 40);
        var b = Lot(lineId, "B", 6, 90);
        b = scenario switch { "duplicate-lot" => b with { BatchNumber = " A " }, "duplicate-client" => b with { ClientLineId = a.ClientLineId }, _ => b with { PurchaseOrderLineId = Guid.NewGuid() } };
        var response = await PostLotsAsync(client, token, poId, [a, b], Guid.NewGuid());
        Assert.False(response.IsSuccessStatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (scenario == "duplicate-client")
        {
            // Validation middleware's established paths identify the frozen request rows.
            Assert.Contains("Lines[1].ClientLineId", problem.GetRawText());
        }
        else
        {
            var rows = problem.GetProperty("rowErrors").EnumerateArray().ToArray();
            Assert.Contains(rows, row => row.GetProperty("clientLineId").GetGuid() == b.ClientLineId && row.GetProperty("lineIndex").GetInt32() == 1);
            if (scenario == "duplicate-lot") Assert.Equal(2, rows.Length);
        }
        await AssertPostingAsync(poId, supplier, 0, 0, 0, 0, 0);
    }

    [Fact]
    public async Task CrossShopKey_DoesNotLeak()
    {
        using var client = CreateClient();
        var tokenA = await CreateShopAsync(client, await RegisterAsync(client));
        var supplierA = await CreateSupplierAsync(client, tokenA, "Shop A supplier");
        var a = await CreatePlacedSingleLinePoAsync(client, tokenA, supplierA, "Shop A");
        var operation = Guid.NewGuid();
        var lot = Lot(a.LineId, "A", 1, 10);
        (await PostLotsAsync(client, tokenA, a.PurchaseOrderId, [lot], operation)).EnsureSuccessStatusCode();
        var tokenB = await CreateShopAsync(client, await RegisterAsync(client));
        using (var lookup = new HttpRequestMessage(HttpMethod.Get, $"/api/financial-operations/purchase-order-receipt/{operation:D}"))
        {
            lookup.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(lookup)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await PostLotsAsync(client, tokenB, a.PurchaseOrderId, [lot], operation)).StatusCode);
        var supplierB = await CreateSupplierAsync(client, tokenB, "Shop B supplier");
        var b = await CreatePlacedSingleLinePoAsync(client, tokenB, supplierB, "Shop B");
        var postedB = await PostLotsAsync(client, tokenB, b.PurchaseOrderId, [Lot(b.LineId, "B", 1, 20)], operation);
        postedB.EnsureSuccessStatusCode();
        var body = await postedB.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(b.PurchaseOrderId, body.GetProperty("purchaseOrderId").GetGuid());
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.OperationReceipts.CountAsync(r => r.OperationType == FinancialOperationType.PurchaseOrderReceipt));
        Assert.Equal(2, await db.PurchaseOrderReceipts.CountAsync());
        Assert.Equal(30, await db.SupplierLedgerEntries.SumAsync(e => e.Amount));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaffOrRevokedMember_CannotReplay(bool revoked)
    {
        using var client = CreateClient();
        var owner = await CreateShopAsync(client, await RegisterAsync(client));
        var supplier = await CreateSupplierAsync(client, owner, "Authority supplier");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, owner, supplier, "Authority");
        var shopId = await GetShopIdFromTokenAsync(client, owner);
        var user = await AddUserAsync(client, owner, shopId, revoked ? "Manager" : "Staff");
        var memberToken = await LoginAsync(client, user.Email, user.Password);
        var operation = Guid.NewGuid();
        var lot = Lot(lineId, "A", 1, 10);
        (await PostLotsAsync(client, owner, poId, [lot], operation)).EnsureSuccessStatusCode();
        if (revoked)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var member = await db.Users.SingleAsync(u => u.Email == user.Email);
            var membership = await db.ShopMemberships.SingleAsync(m => m.UserId == member.Id && m.ShopId == shopId);
            membership.ChangeAuthority(ShopRole.Manager, MembershipStatus.Removed);
            await db.SaveChangesAsync();
        }
        var retry = await PostLotsAsync(client, memberToken, poId, [lot], operation);
        Assert.Equal(revoked ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, retry.StatusCode);
        using var lookup = new HttpRequestMessage(HttpMethod.Get, $"/api/financial-operations/purchase-order-receipt/{operation:D}");
        lookup.Headers.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        Assert.Equal(revoked ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, (await client.SendAsync(lookup)).StatusCode);
        await AssertPostingAsync(poId, supplier, 1, 1, 1, 10, 1);
    }

    [Theory]
    [InlineData("cost", "Lines[1].TotalPurchaseCost")]
    [InlineData("tax", "Lines[1].TaxRatePercent")]
    [InlineData("client-empty", "Lines[1].ClientLineId")]
    public async Task InvalidScalarOrClientId_RejectsWithIndexedValidationAndNoEffects(string scenario, string path)
    {
        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));
        var supplier = await CreateSupplierAsync(client, token, "Scalar supplier");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, token, supplier, "Scalar", 10);
        var b = Lot(lineId, "B", 6, 90);
        b = scenario switch { "cost" => b with { TotalPurchaseCost = -1 }, "tax" => b with { TaxRatePercent = 101 }, _ => b with { ClientLineId = Guid.Empty } };
        var response = await PostLotsAsync(client, token, poId, [Lot(lineId, "A", 4, 40), b], Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains(path, json);
        await AssertPostingAsync(poId, supplier, 0, 0, 0, 0, 0);
    }

    [Fact]
    public async Task ForeignShopItemOnOrder_RejectsBeforeProcessingAnyLot()
    {
        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));
        var supplier = await CreateSupplierAsync(client, token, "Foreign item supplier");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, token, supplier, "Foreign item", 10);
        var foreignToken = await CreateShopAsync(client, await RegisterAsync(client));
        var foreignItem = await CreateItemAsync(client, foreignToken, "Foreign item");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE purchase_order_lines SET item_id = {foreignItem} WHERE id = {lineId}");
        }
        var lot = Lot(lineId, "A", 10, 130);
        var response = await PostLotsAsync(client, token, poId, [lot], Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = Assert.Single(body.GetProperty("rowErrors").EnumerateArray());
        Assert.Equal("PurchaseOrder.LineItemNotFound", error.GetProperty("code").GetString());
        Assert.Equal(lot.ClientLineId, error.GetProperty("clientLineId").GetGuid());
        Assert.Equal(0, error.GetProperty("lineIndex").GetInt32());
        await AssertPostingAsync(poId, supplier, 0, 0, 0, 0, 0);
    }

    [Fact]
    public async Task LegacyRequestWithoutIds_StillReceives()
    {
        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));
        var supplier = await CreateSupplierAsync(client, token, "Legacy supplier");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, token, supplier, "Legacy");
        using var request = CreateReceiveRequest(token, poId, lineId, "LEGACY");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row = body.GetProperty("receipts")[0].GetProperty("lines")[0];
        Assert.Equal(row.GetProperty("receiptLineId").GetGuid(), row.GetProperty("clientLineId").GetGuid());
        await AssertPostingAsync(poId, supplier, 1, 1, 1, 10, 0);
    }

    [Fact]
    public async Task FractionalWireQuantity_IsRejectedWithoutWrites()
    {
        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));
        var supplier = await CreateSupplierAsync(client, token, "Whole quantity");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, token, supplier, "Whole quantity");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{poId}/receipts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { operationId = Guid.NewGuid(), lines = new[] { new { purchaseOrderLineId = lineId, clientLineId = Guid.NewGuid(), barcode = "PRODUCT", batchNumber = "A", quantity = 0.5m, totalPurchaseCost = 10m, mrp = 20m, salesPrice = 15m, taxRatePercent = 5m } } });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(request)).StatusCode);
        await AssertPostingAsync(poId, supplier, 0, 0, 0, 0, 0);
    }

    [Fact]
    public async Task ZeroCostBonusLots_ReceiveStockWithoutPayable()
    {
        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));
        var supplier = await CreateSupplierAsync(client, token, "Bonus stock");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, token, supplier, "Bonus", 10);
        var response = await PostLotsAsync(client, token, poId,
            [Lot(lineId, "A", 4, 0), Lot(lineId, "a", 6, 0) with { TaxIncluded = true, PurchaseTaxIncluded = false }], Guid.NewGuid());
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        await AssertPostingAsync(poId, supplier, 1, 2, 10, 0, 1);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.SupplierLedgerEntries.CountAsync());
        var lower = await db.InventoryBatches.SingleAsync(b => b.BatchNumber == "a");
        Assert.True(lower.TaxIncluded);
        Assert.False(lower.PurchaseTaxIncluded);
    }

    [Fact]
    public async Task ClientLineIdentityMigration_BackfillsExistingLotsAndPermitsLegacyNullWrites()
    {
        using var client = CreateClient();
        var token = await CreateShopAsync(client, await RegisterAsync(client));
        var supplier = await CreateSupplierAsync(client, token, "Migration lots");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, token, supplier, "Migration", 10);
        var response = await PostLotsAsync(client, token, poId, [Lot(lineId, "A", 4, 40), Lot(lineId, "B", 6, 90)]);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
            await migrator.MigrateAsync(migrations[^2]);
            // Exercise the actual upgrade SQL as a schema owner that cannot bypass FORCE RLS.
            // DDL needs ownership, whereas the API's ordinary runtime role is intentionally DML-only.
            await db.Database.OpenConnectionAsync();
            try
            {
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE ROLE receipt_identity_migrator NOLOGIN NOSUPERUSER NOBYPASSRLS;
                    GRANT USAGE, CREATE ON SCHEMA public TO receipt_identity_migrator;
                    GRANT SELECT ON ALL TABLES IN SCHEMA public TO receipt_identity_migrator;
                    GRANT INSERT ON TABLE "__EFMigrationsHistory" TO receipt_identity_migrator;
                    ALTER TABLE purchase_order_receipt_lines OWNER TO receipt_identity_migrator;
                    SET ROLE receipt_identity_migrator;
                    """);
                var role = await db.Database.SqlQueryRaw<bool>(
                    "SELECT NOT rolsuper AND NOT rolbypassrls AS \"Value\" FROM pg_roles WHERE rolname = current_user").SingleAsync();
                Assert.True(role);
                Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
                    "SELECT count(*)::int AS \"Value\" FROM purchase_order_receipt_lines").SingleAsync());
                await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript(migrations[^2], migrations[^1]));
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync("""
                    RESET ROLE;
                    ALTER TABLE purchase_order_receipt_lines OWNER TO CURRENT_USER;
                    DROP OWNED BY receipt_identity_migrator;
                    DROP ROLE receipt_identity_migrator;
                    """);
                await db.Database.CloseConnectionAsync();
            }
            var rows = await db.PurchaseOrderReceiptLines.AsNoTracking().ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, row => Assert.Equal(row.Id, row.ClientLineId));
            // The partial index accepts multiple nulls while an old server is still writing.
            await db.Database.ExecuteSqlRawAsync("UPDATE purchase_order_receipt_lines SET client_line_id = NULL");
        }
        using var detail = new HttpRequestMessage(HttpMethod.Get, $"/api/purchase-orders/{poId}");
        detail.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var result = await client.SendAsync(detail);
        result.EnsureSuccessStatusCode();
        var body = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.All(body.GetProperty("receipts")[0].GetProperty("lines").EnumerateArray(), row =>
            Assert.Equal(row.GetProperty("receiptLineId").GetGuid(), row.GetProperty("clientLineId").GetGuid()));
        await AssertPostingAsync(poId, supplier, 1, 2, 10, 130, 0);
    }

    [Fact]
    public async Task ReceivePurchaseOrder_AsOwner_PersistsLinkedInventoryReceiptAndPartialStatus()
    {
        using var client = CreateClient();
        var ownerToken = await CreateShopAsync(client, await RegisterAsync(client));
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Receive Supplier");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, ownerToken, supplierId, "Partial", expectedQuantity: 2);

        using var receiveRequest = CreateReceiveRequest(ownerToken, poId, lineId, $"B-{Guid.NewGuid():N}", quantity: 1);
        var receiveResponse = await client.SendAsync(receiveRequest);

        receiveResponse.EnsureSuccessStatusCode();
        var body = await receiveResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PartiallyReceived", body.GetProperty("status").GetString());
        Assert.Equal(1, body.GetProperty("lines")[0].GetProperty("receivedQuantity").GetInt32());
        Assert.Equal(1, body.GetProperty("receipts").GetArrayLength());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var receipt = await db.PurchaseOrderReceipts
            .Include(r => r.Lines)
            .SingleAsync(r => r.PurchaseOrderId == poId);
        var receiptLine = Assert.Single(receipt.Lines);
        Assert.Equal(lineId, receiptLine.PurchaseOrderLineId);
        Assert.Equal(10m, receiptLine.TotalPurchaseCost);

        var batch = await db.InventoryBatches.SingleAsync(b => b.Id == receiptLine.InventoryBatchId);
        Assert.Equal(1m, batch.Quantity);
        Assert.Equal(10m, batch.CostPrice);
        Assert.Equal(supplierId, batch.SupplierId);

        var transaction = await db.StockTransactions.SingleAsync(t => t.Id == receiptLine.StockTransactionId);
        Assert.Equal(batch.Id, transaction.InventoryBatchId);
        Assert.Equal(StockTransactionType.In, transaction.TransactionType);
        Assert.Equal(1m, transaction.Quantity);

        var ledgerEntry = await db.SupplierLedgerEntries.SingleAsync(e => e.BatchId == batch.Id);
        Assert.Equal(SupplierLedgerEntryType.GoodsReceived, ledgerEntry.EntryType);
        Assert.Equal(receiptLine.TotalPurchaseCost, ledgerEntry.Amount);
        Assert.Equal(supplierId, ledgerEntry.SupplierId);

        var inventory = await db.Inventory.SingleAsync(i => i.ItemId == batch.ItemId && i.ShopId == batch.ShopId);
        Assert.Equal(receiptLine.Quantity, inventory.Quantity);

        var receiptValue = await db.PurchaseReceiptValues.SingleAsync(r => r.InventoryBatchId == batch.Id);
        Assert.Equal(PurchaseReceiptSource.PurchaseOrder, receiptValue.Source);
        Assert.Equal(receiptLine.Id, receiptValue.PurchaseOrderReceiptLineId);
        Assert.Equal(receiptLine.TotalPurchaseCost, receiptValue.OriginalTotalCost);
        Assert.Equal(supplierId, receiptValue.SupplierId);
    }

    [Fact]
    public async Task ReceivePurchaseOrder_WithMultipleLines_PersistsOneAtomicReceiptAndReceivedStatus()
    {
        using var client = CreateClient();
        var ownerToken = await CreateShopAsync(client, await RegisterAsync(client));
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Multi Receive Supplier");
        var (poId, firstLineId, secondLineId) = await CreatePlacedTwoLinePoAsync(client, ownerToken, supplierId, "Multi");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{poId}/receipts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            referenceNumber = "REF-MULTI",
            notes = "Received via test",
            lines = new[]
            {
                new { purchaseOrderLineId = firstLineId, barcode = $"IT-{firstLineId:N}", batchNumber = $"B-{Guid.NewGuid():N}", quantity = 1, totalPurchaseCost = 10m, mrp = 12m, salesPrice = 11m, taxRatePercent = 5m, taxIncluded = false, purchaseTaxIncluded = false, expiryDate = (DateOnly?)null, manufacturingDate = (DateOnly?)null },
                new { purchaseOrderLineId = secondLineId, barcode = $"IT-{secondLineId:N}", batchNumber = $"B-{Guid.NewGuid():N}", quantity = 1, totalPurchaseCost = 20m, mrp = 24m, salesPrice = 22m, taxRatePercent = 5m, taxIncluded = false, purchaseTaxIncluded = false, expiryDate = (DateOnly?)null, manufacturingDate = (DateOnly?)null },
            },
        });

        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Received", body.GetProperty("status").GetString());
        Assert.Equal(1, body.GetProperty("receipts").GetArrayLength());
        Assert.Equal(2, body.GetProperty("receipts")[0].GetProperty("lines").GetArrayLength());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.PurchaseOrderReceipts.CountAsync(r => r.PurchaseOrderId == poId));
        Assert.Equal(2, await db.PurchaseOrderReceiptLines.CountAsync());
        Assert.Equal(2, await db.InventoryBatches.CountAsync());
        Assert.Equal(2, await db.StockTransactions.CountAsync());
    }

    [Fact]
    public async Task ReceivePurchaseOrder_WhenSecondLineInvalid_PersistsNoReceiptSideEffects()
    {
        using var client = CreateClient();
        var ownerToken = await CreateShopAsync(client, await RegisterAsync(client));
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Atomic Receive Supplier");
        var (poId, firstLineId, secondLineId) = await CreatePlacedTwoLinePoAsync(client, ownerToken, supplierId, "Atomic");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{poId}/receipts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            lines = new[]
            {
                new { purchaseOrderLineId = firstLineId, barcode = $"IT-{firstLineId:N}", batchNumber = $"B-{Guid.NewGuid():N}", quantity = 1, totalPurchaseCost = 10m, mrp = 12m, salesPrice = 11m, taxRatePercent = 5m, taxIncluded = false, purchaseTaxIncluded = false, expiryDate = (DateOnly?)null, manufacturingDate = (DateOnly?)null },
                new { purchaseOrderLineId = secondLineId, barcode = $"IT-{secondLineId:N}", batchNumber = $"B-{Guid.NewGuid():N}", quantity = 2, totalPurchaseCost = 20m, mrp = 24m, salesPrice = 22m, taxRatePercent = 5m, taxIncluded = false, purchaseTaxIncluded = false, expiryDate = (DateOnly?)null, manufacturingDate = (DateOnly?)null },
            },
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.PurchaseOrderReceipts.CountAsync(r => r.PurchaseOrderId == poId));
        Assert.Equal(0, await db.InventoryBatches.CountAsync());
        Assert.Equal(0, await db.StockTransactions.CountAsync());
        Assert.All(await db.PurchaseOrderLines.Where(l => l.PurchaseOrderId == poId).ToListAsync(), line => Assert.Equal(0, line.ReceivedQuantity));
    }

    [Fact]
    public async Task ReceivePurchaseOrder_AsStaff_ReturnsForbidden()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var shopId = await GetShopIdFromTokenAsync(client, ownerToken);
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Staff Receive Supplier");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, ownerToken, supplierId, "Staff");
        var (staffEmail, staffPassword) = await AddStaffAsync(client, ownerToken, shopId);
        var staffToken = await LoginAsync(client, staffEmail, staffPassword);

        using var request = CreateReceiveRequest(staffToken, poId, lineId, $"B-{Guid.NewGuid():N}");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReceivePurchaseOrder_WhenOverRemaining_ReturnsBadRequestAndNoSideEffects()
    {
        using var client = CreateClient();
        var ownerToken = await CreateShopAsync(client, await RegisterAsync(client));
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Over Receive Supplier");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, ownerToken, supplierId, "Over", expectedQuantity: 1);

        using var request = CreateReceiveRequest(ownerToken, poId, lineId, $"B-{Guid.NewGuid():N}", quantity: 2);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.PurchaseOrderReceipts.CountAsync(r => r.PurchaseOrderId == poId));
        Assert.Equal(0, await db.InventoryBatches.CountAsync());
        Assert.Equal(0, await db.StockTransactions.CountAsync());
    }

    [Fact]
    public async Task ReceivePurchaseOrder_WhenDuplicateBatch_RollsBackReceiptAndPoQuantity()
    {
        using var client = CreateClient();
        var ownerToken = await CreateShopAsync(client, await RegisterAsync(client));
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Duplicate Supplier");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, ownerToken, supplierId, "Duplicate", expectedQuantity: 2);
        var batchNumber = $"B-{Guid.NewGuid():N}";

        using var first = CreateReceiveRequest(ownerToken, poId, lineId, batchNumber, quantity: 1);
        (await client.SendAsync(first)).EnsureSuccessStatusCode();
        using var duplicate = CreateReceiveRequest(ownerToken, poId, lineId, batchNumber, quantity: 1);
        var duplicateResponse = await client.SendAsync(duplicate);

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var line = await db.PurchaseOrderLines.SingleAsync(l => l.Id == lineId);
        Assert.Equal(1, line.ReceivedQuantity);
        Assert.Equal(1, await db.PurchaseOrderReceipts.CountAsync(r => r.PurchaseOrderId == poId));
        Assert.Equal(1, await db.InventoryBatches.CountAsync());
        Assert.Equal(1, await db.StockTransactions.CountAsync());
    }

    [Fact]
    public async Task ReceivePurchaseOrder_WhenFinalLineReceived_ReturnsReceivedStatus()
    {
        using var client = CreateClient();
        var ownerToken = await CreateShopAsync(client, await RegisterAsync(client));
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Full Receive Supplier");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, ownerToken, supplierId, "Full");

        using var request = CreateReceiveRequest(ownerToken, poId, lineId, $"B-{Guid.NewGuid():N}");
        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Received", body.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"lines":null}""")]
    [InlineData("""{"lines":[]}""")]
    [InlineData("""{"lines":[{"purchaseOrderLineId":"00000000-0000-0000-0000-000000000001","batchNumber":"B-1","quantity":1,"totalPurchaseCost":10,"mrp":12,"salesPrice":11,"taxRatePercent":5,"taxIncluded":false,"purchaseTaxIncluded":false},{"purchaseOrderLineId":"00000000-0000-0000-0000-000000000001","batchNumber":"B-2","quantity":1,"totalPurchaseCost":10,"mrp":12,"salesPrice":11,"taxRatePercent":5,"taxIncluded":false,"purchaseTaxIncluded":false}]}""")]
    public async Task ReceivePurchaseOrder_WhenMalformedLinePayload_ReturnsBadRequest(string json)
    {
        using var client = CreateClient();
        var ownerToken = await CreateShopAsync(client, await RegisterAsync(client));
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Malformed Supplier");
        var (poId, _) = await CreatePlacedSingleLinePoAsync(client, ownerToken, supplierId, $"Malformed-{Guid.NewGuid():N}");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{poId}/receipts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReceivePurchaseOrder_ConcurrentReceives_OnlyOneConsumesRemainingQuantity()
    {
        using var setupClient = CreateClient();
        var ownerToken = await CreateShopAsync(setupClient, await RegisterAsync(setupClient));
        var supplierId = await CreateSupplierAsync(setupClient, ownerToken, "Concurrent Receive Supplier");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(setupClient, ownerToken, supplierId, "Concurrent");

        var responses = await Task.WhenAll(Enumerable.Range(1, 2).Select(async index =>
        {
            using var client = CreateClient();
            using var request = CreateReceiveRequest(ownerToken, poId, lineId, $"B-{Guid.NewGuid():N}-{index}");
            return await client.SendAsync(request);
        }));

        Assert.Equal(1, responses.Count(response => response.IsSuccessStatusCode));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.BadRequest));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var line = await db.PurchaseOrderLines.SingleAsync(l => l.Id == lineId);
        Assert.Equal(1, line.ReceivedQuantity);
        Assert.Equal(1, await db.PurchaseOrderReceipts.CountAsync(r => r.PurchaseOrderId == poId));
        Assert.Equal(1, await db.InventoryBatches.CountAsync());
        Assert.Equal(1, await db.StockTransactions.CountAsync());
    }

    [Fact]
    public async Task ClosePurchaseOrder_WhenPartiallyReceived_RecordsAuditAndClosedStatus()
    {
        using var client = CreateClient();
        var ownerToken = await CreateShopAsync(client, await RegisterAsync(client));
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Close Supplier");
        var (poId, lineId) = await CreatePlacedSingleLinePoAsync(client, ownerToken, supplierId, "Close", expectedQuantity: 2);

        using var receiveRequest = CreateReceiveRequest(ownerToken, poId, lineId, $"B-{Guid.NewGuid():N}", quantity: 1);
        (await client.SendAsync(receiveRequest)).EnsureSuccessStatusCode();

        using var closeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{poId}/close");
        closeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        closeRequest.Content = JsonContent.Create(new { reason = "Supplier short shipped" });

        var closeResponse = await client.SendAsync(closeRequest);

        closeResponse.EnsureSuccessStatusCode();
        var body = await closeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Closed", body.GetProperty("status").GetString());
        Assert.Equal("Supplier short shipped", body.GetProperty("closeReason").GetString());
        Assert.True(body.GetProperty("closedAt").ValueKind == JsonValueKind.String);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var po = await db.PurchaseOrders.SingleAsync(p => p.Id == poId);
        Assert.Equal(Intelibill.Domain.Enums.PurchaseOrderStatus.Closed, po.Status);
        Assert.Equal("Supplier short shipped", po.CloseReason);
        Assert.NotNull(po.ClosedAt);
        Assert.NotNull(po.ClosedBy);
    }

    [Fact]
    public async Task ClosePurchaseOrder_WhenPlaced_ReturnsBadRequest()
    {
        using var client = CreateClient();
        var ownerToken = await CreateShopAsync(client, await RegisterAsync(client));
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Close Invalid Supplier");
        var (poId, _) = await CreatePlacedSingleLinePoAsync(client, ownerToken, supplierId, "CloseInvalid");

        using var closeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{poId}/close");
        closeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        closeRequest.Content = JsonContent.Create(new { reason = "No stock" });

        var closeResponse = await client.SendAsync(closeRequest);

        Assert.Equal(HttpStatusCode.BadRequest, closeResponse.StatusCode);
    }

    [Fact]
    public async Task PlacePurchaseOrder_AsOwner_ReturnsPlacedStatus()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Active Supplier");
        var draft = await CreateDraftWithSupplierAsync(client, ownerToken, supplierId, "Place");
        var poId = draft.GetProperty("purchaseOrderId").GetGuid();

        using var placeRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/purchase-orders/{poId}/place");
        placeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var placeResponse = await client.SendAsync(placeRequest);
        placeResponse.EnsureSuccessStatusCode();
        var body = await placeResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Placed", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PlacePurchaseOrder_AsStaff_ReturnsForbidden()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Active Supplier 2");
        var draft = await CreateDraftWithSupplierAsync(client, ownerToken, supplierId, "StaffPlace");
        var poId = draft.GetProperty("purchaseOrderId").GetGuid();

        var ownerShopId = await GetShopIdFromTokenAsync(client, ownerToken);
        var (staffEmail, staffPassword) = await AddStaffAsync(client, ownerToken, ownerShopId);
        var staffToken = await LoginAsync(client, staffEmail, staffPassword);

        using var placeRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/purchase-orders/{poId}/place");
        placeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);

        var placeResponse = await client.SendAsync(placeRequest);
        Assert.Equal(HttpStatusCode.Forbidden, placeResponse.StatusCode);
    }

    [Fact]
    public async Task PurchaseOrderLifecycle_AsManager_CanCreateUpdatePlaceReceiveCloseCancelAndDeleteDrafts()
    {
        using var client = CreateClient();
        var ownerToken = await CreateShopAsync(client, await RegisterAsync(client));
        var shopId = await GetShopIdFromTokenAsync(client, ownerToken);
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Manager Supplier");
        var (managerEmail, managerPassword) = await AddManagerAsync(client, ownerToken, shopId);
        var managerToken = await LoginAsync(client, managerEmail, managerPassword);

        var draft = await CreateDraftWithSupplierAsync(client, managerToken, supplierId, "ManagerLifecycle");
        var poId = draft.GetProperty("purchaseOrderId").GetGuid();
        var itemId = draft.GetProperty("lines")[0].GetProperty("itemId").GetGuid();

        using var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/purchase-orders/{poId}");
        updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        updateRequest.Content = JsonContent.Create(new
        {
            supplierId,
            notes = "Manager updated",
            lines = new[] { new { itemId, description = "Manager Item Updated", expectedQuantity = 2, unitCost = 15m } },
        });
        var updateResponse = await client.SendAsync(updateRequest);
        Assert.True(updateResponse.IsSuccessStatusCode, await updateResponse.Content.ReadAsStringAsync());
        var updated = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(30m, updated.GetProperty("expectedTotal").GetDecimal());
        Assert.Equal("Manager updated", updated.GetProperty("notes").GetString());

        using var placeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{poId}/place");
        placeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        var placeResponse = await client.SendAsync(placeRequest);
        placeResponse.EnsureSuccessStatusCode();
        var placed = await placeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Placed", placed.GetProperty("status").GetString());

        var lineId = placed.GetProperty("lines")[0].GetProperty("lineId").GetGuid();
        using var receiveRequest = CreateReceiveRequest(managerToken, poId, lineId, $"B-{Guid.NewGuid():N}", quantity: 1);
        var receiveResponse = await client.SendAsync(receiveRequest);
        receiveResponse.EnsureSuccessStatusCode();
        Assert.Equal("PartiallyReceived", (await receiveResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        using var closeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{poId}/close");
        closeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        closeRequest.Content = JsonContent.Create(new { reason = "Manager short close" });
        var closeResponse = await client.SendAsync(closeRequest);
        closeResponse.EnsureSuccessStatusCode();
        Assert.Equal("Closed", (await closeResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var cancelDraft = await CreateDraftWithSupplierAsync(client, managerToken, supplierId, "ManagerCancel");
        var cancelPoId = cancelDraft.GetProperty("purchaseOrderId").GetGuid();
        using var cancelPlaceRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{cancelPoId}/place");
        cancelPlaceRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        (await client.SendAsync(cancelPlaceRequest)).EnsureSuccessStatusCode();
        using var cancelRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{cancelPoId}/cancel");
        cancelRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        cancelRequest.Content = JsonContent.Create(new { reason = "Manager cancelled" });
        var cancelResponse = await client.SendAsync(cancelRequest);
        cancelResponse.EnsureSuccessStatusCode();
        Assert.Equal("Cancelled", (await cancelResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var deleteDraft = await CreateDraftWithSupplierAsync(client, managerToken, supplierId, "ManagerDelete");
        var deletePoId = deleteDraft.GetProperty("purchaseOrderId").GetGuid();
        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/purchase-orders/{deletePoId}");
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(deleteRequest)).StatusCode);
    }

    [Fact]
    public async Task PurchaseOrderLifecycle_AsStaffCanListAndDetailButCannotMutate()
    {
        using var client = CreateClient();
        var ownerToken = await CreateShopAsync(client, await RegisterAsync(client));
        var shopId = await GetShopIdFromTokenAsync(client, ownerToken);
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Staff Matrix Supplier");
        var draft = await CreateDraftWithSupplierAsync(client, ownerToken, supplierId, "StaffMatrixDraft");
        var draftPoId = draft.GetProperty("purchaseOrderId").GetGuid();
        var draftLineItemId = draft.GetProperty("lines")[0].GetProperty("itemId").GetGuid();
        var placed = await CreatePlacedSingleLinePoAsync(client, ownerToken, supplierId, "StaffMatrixPlaced", expectedQuantity: 2);

        var (staffEmail, staffPassword) = await AddStaffAsync(client, ownerToken, shopId);
        var staffToken = await LoginAsync(client, staffEmail, staffPassword);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/purchase-orders");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        (await client.SendAsync(listRequest)).EnsureSuccessStatusCode();

        using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/purchase-orders/{draftPoId}");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        (await client.SendAsync(detailRequest)).EnsureSuccessStatusCode();

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/purchase-orders");
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        createRequest.Content = JsonContent.Create(new
        {
            supplierId,
            lines = new[] { new { itemId = draftLineItemId, description = "Staff create", expectedQuantity = 1, unitCost = 10m } },
        });

        using var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/purchase-orders/{draftPoId}");
        updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        updateRequest.Content = JsonContent.Create(new
        {
            supplierId,
            lines = new[] { new { itemId = draftLineItemId, description = "Staff update", expectedQuantity = 1, unitCost = 10m } },
        });

        using var placeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{draftPoId}/place");
        placeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/purchase-orders/{draftPoId}");
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);

        using var cancelRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{placed.PurchaseOrderId}/cancel");
        cancelRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        cancelRequest.Content = JsonContent.Create(new { reason = "Staff cancel" });

        using var receiveRequest = CreateReceiveRequest(staffToken, placed.PurchaseOrderId, placed.LineId, $"B-{Guid.NewGuid():N}", quantity: 1);

        using var closeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{placed.PurchaseOrderId}/close");
        closeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        closeRequest.Content = JsonContent.Create(new { reason = "Staff close" });

        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(createRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(updateRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(placeRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(deleteRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(cancelRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(receiveRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(closeRequest)).StatusCode);
    }

    [Fact]
    public async Task PlacePurchaseOrder_WithoutSupplier_ReturnsBadRequest()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var draft = await CreateDraftAsync(client, ownerToken, "NoSupplier");
        var poId = draft.GetProperty("purchaseOrderId").GetGuid();

        using var placeRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/purchase-orders/{poId}/place");
        placeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var placeResponse = await client.SendAsync(placeRequest);
        Assert.Equal(HttpStatusCode.BadRequest, placeResponse.StatusCode);
    }

    [Fact]
    public async Task UpdatePurchaseOrderDraft_WhenPlaced_ReturnsBadRequest()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Active Supplier 3");
        var draft = await CreateDraftWithSupplierAsync(client, ownerToken, supplierId, "EditBlocked");
        var poId = draft.GetProperty("purchaseOrderId").GetGuid();

        using var placeRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/purchase-orders/{poId}/place");
        placeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        await (await client.SendAsync(placeRequest)).EnsureSuccessStatusCode().Content.ReadAsStringAsync();

        using var updateRequest = new HttpRequestMessage(
            HttpMethod.Put, $"/api/purchase-orders/{poId}");
        updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        updateRequest.Content = JsonContent.Create(new
        {
            lines = new[] { new { description = "Item A", expectedQuantity = 5, unitCost = 200m } },
        });

        var updateResponse = await client.SendAsync(updateRequest);
        Assert.Equal(HttpStatusCode.BadRequest, updateResponse.StatusCode);
    }

    [Fact]
    public async Task DeletePurchaseOrderDraft_AsOwner_ReturnsNoContent()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var draft = await CreateDraftAsync(client, ownerToken, "DeleteMe");
        var poId = draft.GetProperty("purchaseOrderId").GetGuid();

        using var deleteRequest = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/purchase-orders/{poId}");
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var deleteResponse = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task DeletePurchaseOrderDraft_WhenPlaced_ReturnsBadRequest()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Active Supplier 4");
        var draft = await CreateDraftWithSupplierAsync(client, ownerToken, supplierId, "PlacedDelete");
        var poId = draft.GetProperty("purchaseOrderId").GetGuid();

        using var placeRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/purchase-orders/{poId}/place");
        placeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        (await client.SendAsync(placeRequest)).EnsureSuccessStatusCode();

        using var deleteRequest = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/purchase-orders/{poId}");
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var deleteResponse = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.BadRequest, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task CancelPurchaseOrder_PlacedWithNoReceipts_ReturnsCancelledStatus()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Active Supplier 5");
        var draft = await CreateDraftWithSupplierAsync(client, ownerToken, supplierId, "Cancel");
        var poId = draft.GetProperty("purchaseOrderId").GetGuid();

        using var placeRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/purchase-orders/{poId}/place");
        placeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        (await client.SendAsync(placeRequest)).EnsureSuccessStatusCode();

        using var cancelRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/purchase-orders/{poId}/cancel");
        cancelRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        cancelRequest.Content = JsonContent.Create(new { reason = "Supplier unavailable" });

        var cancelResponse = await client.SendAsync(cancelRequest);
        cancelResponse.EnsureSuccessStatusCode();
        var body = await cancelResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Cancelled", body.GetProperty("status").GetString());
        Assert.Equal("Supplier unavailable", body.GetProperty("cancellationReason").GetString());
    }

    [Fact]
    public async Task CancelPurchaseOrder_AsStaff_ReturnsForbidden()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var draft = await CreateDraftAsync(client, ownerToken, "StaffCancel");
        var poId = draft.GetProperty("purchaseOrderId").GetGuid();

        var ownerShopId = await GetShopIdFromTokenAsync(client, ownerToken);
        var (staffEmail, staffPassword) = await AddStaffAsync(client, ownerToken, ownerShopId);
        var staffToken = await LoginAsync(client, staffEmail, staffPassword);

        using var cancelRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/purchase-orders/{poId}/cancel");
        cancelRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        cancelRequest.Content = JsonContent.Create(new { reason = "attempt" });

        var cancelResponse = await client.SendAsync(cancelRequest);
        Assert.Equal(HttpStatusCode.Forbidden, cancelResponse.StatusCode);
    }

    [Fact]
    public async Task ListPurchaseOrders_StaffCanView()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        await CreateDraftAsync(client, ownerToken, "ForStaffView");

        var ownerShopId = await GetShopIdFromTokenAsync(client, ownerToken);
        var (staffEmail, staffPassword) = await AddStaffAsync(client, ownerToken, ownerShopId);
        var staffToken = await LoginAsync(client, staffEmail, staffPassword);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/purchase-orders");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var listResponse = await client.SendAsync(listRequest);

        listResponse.EnsureSuccessStatusCode();
        var body = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task CancelPurchaseOrder_Draft_ReturnsBadRequest()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var draft = await CreateDraftAsync(client, ownerToken, "CancelDraftAttempt");
        var poId = draft.GetProperty("purchaseOrderId").GetGuid();

        using var cancelRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/purchase-orders/{poId}/cancel");
        cancelRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        cancelRequest.Content = JsonContent.Create(new { reason = "Attempted cancel of draft" });

        var cancelResponse = await client.SendAsync(cancelRequest);
        Assert.Equal(HttpStatusCode.BadRequest, cancelResponse.StatusCode);
    }

    [Fact]
    public async Task CancelPurchaseOrder_WithBlankReason_ReturnsBadRequest()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Active Supplier 6");
        var draft = await CreateDraftWithSupplierAsync(client, ownerToken, supplierId, "BlankReason");
        var poId = draft.GetProperty("purchaseOrderId").GetGuid();

        using var placeRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/purchase-orders/{poId}/place");
        placeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        (await client.SendAsync(placeRequest)).EnsureSuccessStatusCode();

        using var cancelRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/purchase-orders/{poId}/cancel");
        cancelRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        cancelRequest.Content = JsonContent.Create(new { reason = "   " });

        var cancelResponse = await client.SendAsync(cancelRequest);
        Assert.Equal(HttpStatusCode.BadRequest, cancelResponse.StatusCode);
    }

    [Fact]
    public async Task CancelPurchaseOrder_WithNullReason_ReturnsBadRequest()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Active Supplier 7");
        var draft = await CreateDraftWithSupplierAsync(client, ownerToken, supplierId, "NullReason");
        var poId = draft.GetProperty("purchaseOrderId").GetGuid();

        using var placeRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/purchase-orders/{poId}/place");
        placeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        (await client.SendAsync(placeRequest)).EnsureSuccessStatusCode();

        using var cancelRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/purchase-orders/{poId}/cancel");
        cancelRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        cancelRequest.Content = JsonContent.Create(new { reason = (string?)null });

        var cancelResponse = await client.SendAsync(cancelRequest);
        Assert.Equal(HttpStatusCode.BadRequest, cancelResponse.StatusCode);
    }

    [Fact]
    public async Task CancelPurchaseOrder_WithOversizedReason_ReturnsBadRequest()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Active Supplier 8");
        var draft = await CreateDraftWithSupplierAsync(client, ownerToken, supplierId, "LongReason");
        var poId = draft.GetProperty("purchaseOrderId").GetGuid();

        using var placeRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/purchase-orders/{poId}/place");
        placeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        (await client.SendAsync(placeRequest)).EnsureSuccessStatusCode();

        using var cancelRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/purchase-orders/{poId}/cancel");
        cancelRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        cancelRequest.Content = JsonContent.Create(new { reason = new string('A', 501) });

        var cancelResponse = await client.SendAsync(cancelRequest);
        Assert.Equal(HttpStatusCode.BadRequest, cancelResponse.StatusCode);
    }
}
