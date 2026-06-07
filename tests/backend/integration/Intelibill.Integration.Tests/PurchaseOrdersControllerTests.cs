using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;
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
        var response = await client.PostAsJsonAsync("/api/auth/register/email", new
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

    private static async Task<(string Email, string Password)> AddStaffAsync(HttpClient client, string ownerToken, Guid shopId)
    {
        var staffEmail = UniqueEmail();
        var staffPassword = "Pass123!Aa";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            shopIds = new[] { shopId },
            email = staffEmail,
            firstName = "Shop",
            lastName = "Staff",
            phoneNumber = UniquePhone(),
            password = staffPassword,
            confirmPassword = staffPassword,
            role = "Staff",
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return (staffEmail, staffPassword);
    }

    private static async Task<JsonElement> CreateDraftAsync(
        HttpClient client,
        string token,
        string prefix,
        string? supplierName = null,
        string? supplierReference = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/purchase-orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            notes = $"Draft for {prefix}",
            supplierName,
            supplierReference,
            lines = new[]
            {
                new { description = "Item A", expectedQuantity = 3, unitCost = 100m },
                new { description = "Item B", expectedQuantity = 2, unitCost = 25m },
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

        var ownerShopId = await GetShopIdFromTokenAsync(client, ownerToken);
        var (staffEmail, staffPassword) = await AddStaffAsync(client, ownerToken, ownerShopId);
        var staffToken = await LoginAsync(client, staffEmail, staffPassword);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/purchase-orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        request.Content = JsonContent.Create(new
        {
            notes = "Staff attempt",
            lines = new[] { new { description = "Item", expectedQuantity = 1, unitCost = 10m } },
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
        var orderDate = DateOnly.FromDateTime(createdAt.UtcDateTime);
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
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/purchase-orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            supplierId,
            notes = $"Draft for {prefix}",
            lines = new[]
            {
                new { description = "Item A", expectedQuantity = 3, unitCost = 100m },
            },
        });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
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
}
