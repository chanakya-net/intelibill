using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelibill.Integration.Tests;

public class SuppliersControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public SuppliersControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static string UniqueEmail() => $"suppliers-{Guid.NewGuid():N}@test.com";

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email = UniqueEmail(),
            password = "Pass123!",
            firstName = "Test",
            lastName = "User",
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static async Task<(Guid ShopId, string OwnerToken)> CreateShopAsync(HttpClient client, string token)
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
        return (body.GetProperty("activeShopId").GetGuid(), body.GetProperty("accessToken").GetString()!);
    }

    [Fact]
    public async Task GetSuppliers_WithoutAuth_Returns401()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/suppliers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSupplierLedger_WithoutAuth_Returns401()
    {
        using var client = CreateClient();

        var response = await client.GetAsync($"/api/suppliers/{Guid.NewGuid()}/ledger");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AddSupplier_AsOwner_Returns201_AndListReturnsSupplier()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var addRequest = new HttpRequestMessage(HttpMethod.Post, "/api/suppliers");
        addRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        addRequest.Content = JsonContent.Create(new
        {
            name = "Fresh Foods",
            contactPersonName = "Ramesh",
            contactPersonPhone = "+919999999999",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pin = "560001",
            isActive = true,
            isPreferred = true,
        });

        var addResponse = await client.SendAsync(addRequest);
        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/suppliers");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse = await client.SendAsync(listRequest);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var body = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetArrayLength() >= 1);
        Assert.Equal("Fresh Foods", body[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AddSupplier_WithoutOwnerClaim_Returns403()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/suppliers");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name = "Fresh Foods",
            contactPersonName = "Ramesh",
            contactPersonPhone = "+919999999999",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pin = "560001",
            isActive = true,
            isPreferred = false,
        });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EditSupplier_AsOwner_UpdatesSupplierAndReturns200()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var addRequest = new HttpRequestMessage(HttpMethod.Post, "/api/suppliers");
        addRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        addRequest.Content = JsonContent.Create(new
        {
            name = "Fresh Foods",
            contactPersonName = "Ramesh",
            contactPersonPhone = "+919999999999",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pin = "560001",
            isActive = true,
            isPreferred = true,
        });
        var addResponse = await client.SendAsync(addRequest);
        addResponse.EnsureSuccessStatusCode();

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/suppliers");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse = await client.SendAsync(listRequest);
        var suppliers = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var supplierId = suppliers[0].GetProperty("supplierId").GetGuid();

        using var editRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/suppliers/{supplierId}");
        editRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        editRequest.Content = JsonContent.Create(new
        {
            name = "Fresh Foods Updated",
            contactPersonName = "Suresh",
            contactPersonPhone = "+918888888888",
            address = "43 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pin = "560002",
            isActive = false,
            isPreferred = false,
        });
        
        var editResponse = await client.SendAsync(editRequest);
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);

        using var verifyRequest = new HttpRequestMessage(HttpMethod.Get, "/api/suppliers");
        verifyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var verifyResponse = await client.SendAsync(verifyRequest);
        var verifiedSuppliers = await verifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        
        Assert.True(verifiedSuppliers.GetArrayLength() >= 1);
        Assert.Equal("Fresh Foods Updated", verifiedSuppliers[0].GetProperty("name").GetString());
        Assert.Equal("Suresh", verifiedSuppliers[0].GetProperty("contactPersonName").GetString());
        Assert.False(verifiedSuppliers[0].GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task MakePayment_AsOwner_Returns200_AndLedgerReturnsEntry()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var addRequest = new HttpRequestMessage(HttpMethod.Post, "/api/suppliers");
        addRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        addRequest.Content = JsonContent.Create(new
        {
            name = "Fresh Foods",
            contactPersonName = "Ramesh",
            contactPersonPhone = "+919999999999",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pin = "560001",
            isActive = true,
            isPreferred = true,
        });
        await client.SendAsync(addRequest);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/suppliers");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse = await client.SendAsync(listRequest);
        var suppliers = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var supplierId = suppliers[0].GetProperty("supplierId").GetGuid();

        using var paymentRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/suppliers/{supplierId}/payments");
        paymentRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        paymentRequest.Content = JsonContent.Create(new
        {
            amount = 1500.50m,
            paymentDate = "2024-01-01",
            notes = "Advance payment"
        });

        var paymentResponse = await client.SendAsync(paymentRequest);
        Assert.Equal(HttpStatusCode.OK, paymentResponse.StatusCode);

        using var ledgerRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/suppliers/{supplierId}/ledger");
        ledgerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var ledgerResponse = await client.SendAsync(ledgerRequest);
        Assert.Equal(HttpStatusCode.OK, ledgerResponse.StatusCode);

        var ledgerEntries = await ledgerResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(ledgerEntries.GetArrayLength() >= 1);
        
        var paymentEntry = ledgerEntries[0];
        Assert.Equal(1500.50m, paymentEntry.GetProperty("amount").GetDecimal());
        Assert.Equal("Advance payment", paymentEntry.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task MakePayment_WithoutOwnerOrManagerClaim_Returns403()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/suppliers/{Guid.NewGuid()}/payments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            amount = 1500.50m,
            paymentDate = "2024-01-01",
            notes = "Advance payment"
        });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MakePayment_CreatesExpenseInBackground()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var addRequest = new HttpRequestMessage(HttpMethod.Post, "/api/suppliers");
        addRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        addRequest.Content = JsonContent.Create(new
        {
            name = "Fresh Foods",
            contactPersonName = "Ramesh",
            contactPersonPhone = "+919999999999",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pin = "560001",
            isActive = true,
            isPreferred = true,
        });
        await client.SendAsync(addRequest);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/suppliers");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse = await client.SendAsync(listRequest);
        var suppliers = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var supplierId = suppliers[0].GetProperty("supplierId").GetGuid();

        using var paymentRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/suppliers/{supplierId}/payments");
        paymentRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        paymentRequest.Content = JsonContent.Create(new
        {
            amount = 1500.50m,
            paymentDate = "2024-01-01",
            notes = "Advance payment"
        });

        var paymentResponse = await client.SendAsync(paymentRequest);
        Assert.Equal(HttpStatusCode.OK, paymentResponse.StatusCode);

        var paymentBody = await paymentResponse.Content.ReadFromJsonAsync<JsonElement>();
        var ledgerEntryId = paymentBody.GetProperty("id").GetGuid();

        using var expensesRequest = new HttpRequestMessage(HttpMethod.Get, "/api/expenses");
        expensesRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var expensesResponse = await client.SendAsync(expensesRequest);
        Assert.Equal(HttpStatusCode.OK, expensesResponse.StatusCode);

        var expensesBody = await expensesResponse.Content.ReadFromJsonAsync<JsonElement>();
        var items = expensesBody.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);

        var expense = items.EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("amount").GetDecimal() == 1500.50m);
        Assert.NotEqual(default, expense);
        Assert.Equal("Supplier Payments", expense.GetProperty("categoryName").GetString());
        Assert.Equal("Supplier", expense.GetProperty("paidTo").GetString());

        var expenseId = expense.GetProperty("id").GetGuid();
        using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/expenses/{expenseId}");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var detailResponse = await client.SendAsync(detailRequest);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1500.50m, detail.GetProperty("amount").GetDecimal());
        Assert.Equal("Advance payment", detail.GetProperty("description").GetString());
        Assert.Equal(ledgerEntryId, detail.GetProperty("supplierLedgerEntryId").GetGuid());
    }

    [Fact]
    public async Task GetSuppliers_BalanceDue_ReflectsPaymentMadeEntries()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var addRequest = new HttpRequestMessage(HttpMethod.Post, "/api/suppliers");
        addRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        addRequest.Content = JsonContent.Create(new
        {
            name = "Balance Test Supplier",
            contactPersonName = (string?)null,
            contactPersonPhone = (string?)null,
            address = "1 Test St",
            city = "Bengaluru",
            state = "Karnataka",
            pin = "560001",
            isActive = true,
            isPreferred = false,
        });
        var addResponse = await client.SendAsync(addRequest);
        addResponse.EnsureSuccessStatusCode();

        using var listRequest1 = new HttpRequestMessage(HttpMethod.Get, "/api/suppliers?include_system=false");
        listRequest1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse1 = await client.SendAsync(listRequest1);
        var suppliers1 = await listResponse1.Content.ReadFromJsonAsync<JsonElement>();
        var supplierId = suppliers1[0].GetProperty("supplierId").GetGuid();

        // Balance with no entries should be 0
        Assert.Equal(0m, suppliers1[0].GetProperty("balanceDue").GetDecimal());

        using var paymentRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/suppliers/{supplierId}/payments");
        paymentRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        paymentRequest.Content = JsonContent.Create(new
        {
            amount = 750m,
            paymentDate = "2026-04-01",
            notes = "Test payment"
        });
        var paymentResponse = await client.SendAsync(paymentRequest);
        paymentResponse.EnsureSuccessStatusCode();

        using var listRequest2 = new HttpRequestMessage(HttpMethod.Get, "/api/suppliers?include_system=false");
        listRequest2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse2 = await client.SendAsync(listRequest2);
        var suppliers2 = await listResponse2.Content.ReadFromJsonAsync<JsonElement>();

        // PaymentMade reduces balance → -750
        Assert.Equal(-750m, suppliers2[0].GetProperty("balanceDue").GetDecimal());

        // Ledger total must equal balanceDue
        using var ledgerRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/suppliers/{supplierId}/ledger");
        ledgerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var ledgerResponse = await client.SendAsync(ledgerRequest);
        ledgerResponse.EnsureSuccessStatusCode();
        var ledger = await ledgerResponse.Content.ReadFromJsonAsync<JsonElement>();

        // entryType 2 = PaymentMade (stored positive, represents debit → negate for net balance)
        var ledgerTotal = ledger.EnumerateArray()
            .Sum(e =>
            {
                var amount = e.GetProperty("amount").GetDecimal();
                var entryType = e.GetProperty("entryType").GetInt32();
                return entryType == 2 ? -amount : amount;
            });

        Assert.Equal(suppliers2[0].GetProperty("balanceDue").GetDecimal(), ledgerTotal);
    }
}