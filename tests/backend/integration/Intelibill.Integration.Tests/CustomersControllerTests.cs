using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class CustomersControllerTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    private static string UniqueEmail() => $"customers-{Guid.NewGuid():N}@test.com";
    private static string UniqueBarcode() => $"CUST-{Guid.NewGuid():N}";

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

    private static async Task<Guid> GetShopIdFromTokenAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/shops/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.EnumerateArray().First().GetProperty("shopId").GetGuid();
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

    [Fact]
    public async Task GetCustomers_WithoutAuth_Returns401()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/customers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AddCustomer_Returns201_AndListReturnsCustomer()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var addRequest = new HttpRequestMessage(HttpMethod.Post, "/api/customers");
        addRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        addRequest.Content = JsonContent.Create(new
        {
            name = "John Doe",
            phoneNumber = "+919876543210",
            address = "12 Market Road",
            isActive = true
        });

        var addResponse = await client.SendAsync(addRequest);
        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);
        var addBody = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0m, addBody.GetProperty("creditLimit").GetDecimal());

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/customers");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse = await client.SendAsync(listRequest);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var body = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetArrayLength() >= 1);
        Assert.Equal("John Doe", body[0].GetProperty("name").GetString());
        Assert.Equal(0m, body[0].GetProperty("creditLimit").GetDecimal());
    }

    [Fact]
    public async Task EditCustomer_UpdatesCustomerAndReturns200()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var addRequest = new HttpRequestMessage(HttpMethod.Post, "/api/customers");
        addRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        addRequest.Content = JsonContent.Create(new
        {
            name = "John Doe",
            phoneNumber = "+919876543210",
            address = "12 Market Road",
            isActive = true
        });
        var addResponse = await client.SendAsync(addRequest);
        addResponse.EnsureSuccessStatusCode();

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/customers");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse = await client.SendAsync(listRequest);
        var customers = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var customerId = customers[0].GetProperty("customerId").GetGuid();

        using var editRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/customers/{customerId}");
        editRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        editRequest.Content = JsonContent.Create(new
        {
            name = "John Doe Updated",
            phoneNumber = "+918888888888",
            address = "43 MG Road",
            isActive = false,
            creditLimit = 1250.50m
        });
        
        var editResponse = await client.SendAsync(editRequest);
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        var editBody = await editResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1250.50m, editBody.GetProperty("creditLimit").GetDecimal());

        using var verifyRequest = new HttpRequestMessage(HttpMethod.Get, "/api/customers");
        verifyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var verifyResponse = await client.SendAsync(verifyRequest);
        var verifiedCustomers = await verifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        
        Assert.True(verifiedCustomers.GetArrayLength() >= 1);
        Assert.Equal("John Doe Updated", verifiedCustomers[0].GetProperty("name").GetString());
        Assert.Equal("+918888888888", verifiedCustomers[0].GetProperty("phoneNumber").GetString());
        Assert.False(verifiedCustomers[0].GetProperty("isActive").GetBoolean());
        Assert.Equal(1250.50m, verifiedCustomers[0].GetProperty("creditLimit").GetDecimal());
    }

    // ======================= NEW: CUSTOMER ACCOUNT =======================

    [Fact]
    public async Task GetCustomerAccount_ReturnsAccountWithSalesAndPayments()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var addCustomerRequest = new HttpRequestMessage(HttpMethod.Post, "/api/customers");
        addCustomerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        addCustomerRequest.Content = JsonContent.Create(new
        {
            name = "Account Customer",
            phoneNumber = "+919876543210",
            address = "12 Market Road",
            isActive = true,
        });
        var addCustomerResponse = await client.SendAsync(addCustomerRequest);
        Assert.Equal(HttpStatusCode.Created, addCustomerResponse.StatusCode);
        var addCustomerBody = await addCustomerResponse.Content.ReadFromJsonAsync<JsonElement>();

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/customers");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse = await client.SendAsync(listRequest);
        var customers = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var customerId = customers[0].GetProperty("customerId").GetGuid();

        using var accountRequest = new HttpRequestMessage(
            HttpMethod.Get, $"/api/customers/{customerId}/account");
        accountRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var accountResponse = await client.SendAsync(accountRequest);

        Assert.Equal(HttpStatusCode.OK, accountResponse.StatusCode);
        var account = await accountResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(customerId, account.GetProperty("customerId").GetGuid());
        Assert.Equal("Account Customer", account.GetProperty("name").GetString());
        Assert.Equal(0m, account.GetProperty("outstandingDue").GetDecimal());
    }

    [Fact]
    public async Task GetCustomerAccount_StaffGetsForbidden()
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var (_, ownerScopedToken) = await CreateShopAsync(client, ownerToken);

        var staffEmail = UniqueEmail();
        var staffPassword = "Pass123!Aa";
        using var addUserRequest = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        addUserRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerScopedToken);
        addUserRequest.Content = JsonContent.Create(new
        {
            shopIds = new[] { await GetShopIdFromTokenAsync(client, ownerScopedToken) },
            email = staffEmail,
            firstName = "Staff",
            lastName = "Account",
            phoneNumber = "+919876543211",
            password = staffPassword,
            confirmPassword = staffPassword,
            role = "Staff",
        });
        var addUserResponse = await client.SendAsync(addUserRequest);
        addUserResponse.EnsureSuccessStatusCode();

        var staffToken = await LoginAsync(client, staffEmail, staffPassword);

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/customers/{Guid.NewGuid()}/account");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCustomerAccount_OtherShop_ReturnsNotFound()
    {
        using var client = CreateClient();
        var tokenA = await RegisterAsync(client);
        var (_, ownerTokenA) = await CreateShopAsync(client, tokenA);

        using var addCustomerRequest = new HttpRequestMessage(HttpMethod.Post, "/api/customers");
        addCustomerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenA);
        addCustomerRequest.Content = JsonContent.Create(new
        {
            name = "Cross Shop Customer",
            phoneNumber = "+919876543210",
            address = "12 Market Road",
            isActive = true,
        });
        var addResponse = await client.SendAsync(addCustomerRequest);
        addResponse.EnsureSuccessStatusCode();

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/customers");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenA);
        var listResponse = await client.SendAsync(listRequest);
        var customers = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var customerId = customers[0].GetProperty("customerId").GetGuid();

        var tokenB = await RegisterAsync(client);
        var (_, ownerTokenB) = await CreateShopAsync(client, tokenB);

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/customers/{customerId}/account");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ======================= NEW: CUSTOMER PAYMENTS =======================

    [Fact]
    public async Task RecordCustomerPayment_ReturnsLedgerEntry()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var addCustomerRequest = new HttpRequestMessage(HttpMethod.Post, "/api/customers");
        addCustomerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        addCustomerRequest.Content = JsonContent.Create(new
        {
            name = "Payment Customer",
            phoneNumber = "+919876543210",
            address = "12 Market Road",
            isActive = true,
        });
        var addCustomerResponse = await client.SendAsync(addCustomerRequest);
        Assert.Equal(HttpStatusCode.Created, addCustomerResponse.StatusCode);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/customers");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse = await client.SendAsync(listRequest);
        var customers = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var customerId = customers[0].GetProperty("customerId").GetGuid();

        using var paymentRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/customers/{customerId}/payments");
        paymentRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        paymentRequest.Content = JsonContent.Create(new
        {
            amount = 500m,
            paymentDate = "2026-04-15",
            notes = "Partial payment",
        });

        var paymentResponse = await client.SendAsync(paymentRequest);
        Assert.Equal(HttpStatusCode.OK, paymentResponse.StatusCode);

        var paymentBody = await paymentResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(500m, paymentBody.GetProperty("amount").GetDecimal());
        Assert.Equal("Partial payment", paymentBody.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task RecordCustomerPayment_AccountReflectsPayment()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var addCustomerRequest = new HttpRequestMessage(HttpMethod.Post, "/api/customers");
        addCustomerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        addCustomerRequest.Content = JsonContent.Create(new
        {
            name = "Ledger Customer",
            phoneNumber = "+919876543210",
            address = "12 Market Road",
            isActive = true,
        });
        var addCustomerResponse = await client.SendAsync(addCustomerRequest);
        Assert.Equal(HttpStatusCode.Created, addCustomerResponse.StatusCode);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/customers");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse = await client.SendAsync(listRequest);
        var customers = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var customerId = customers[0].GetProperty("customerId").GetGuid();

        using var paymentRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/customers/{customerId}/payments");
        paymentRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        paymentRequest.Content = JsonContent.Create(new
        {
            amount = 300m,
            paymentDate = "2026-04-15",
            notes = "Payment",
        });
        var paymentResponse = await client.SendAsync(paymentRequest);
        Assert.Equal(HttpStatusCode.OK, paymentResponse.StatusCode);

        using var accountRequest = new HttpRequestMessage(
            HttpMethod.Get, $"/api/customers/{customerId}/account");
        accountRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var accountResponse = await client.SendAsync(accountRequest);

        Assert.Equal(HttpStatusCode.OK, accountResponse.StatusCode);
        var account = await accountResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(-300m, account.GetProperty("outstandingDue").GetDecimal());
    }

    [Fact]
    public async Task RecordCustomerPayment_StaffGetsForbidden()
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var (_, ownerScopedToken) = await CreateShopAsync(client, ownerToken);

        var staffEmail = UniqueEmail();
        var staffPassword = "Pass123!Aa";
        using var addUserRequest = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        addUserRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerScopedToken);
        addUserRequest.Content = JsonContent.Create(new
        {
            shopIds = new[] { await GetShopIdFromTokenAsync(client, ownerScopedToken) },
            email = staffEmail,
            firstName = "Staff",
            lastName = "Payment",
            phoneNumber = "+919876543211",
            password = staffPassword,
            confirmPassword = staffPassword,
            role = "Staff",
        });
        var addUserResponse = await client.SendAsync(addUserRequest);
        addUserResponse.EnsureSuccessStatusCode();

        var staffToken = await LoginAsync(client, staffEmail, staffPassword);

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/customers/{Guid.NewGuid()}/payments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        request.Content = JsonContent.Create(new
        {
            amount = 100m,
            paymentDate = "2026-04-15",
            notes = (string?)null,
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
