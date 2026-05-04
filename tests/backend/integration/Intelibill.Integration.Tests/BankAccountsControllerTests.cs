using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class BankAccountsControllerTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    private static string UniqueEmail() => $"bank-{Guid.NewGuid():N}@test.com";
    private static string UniqueIfsc() => $"HDFC0{Guid.NewGuid():N}"[..11].ToUpperInvariant();
    private static string UniqueAccountNumber() => $"{Math.Abs(Guid.NewGuid().GetHashCode()):D10}{Math.Abs(Guid.NewGuid().GetHashCode()):D10}"[..14];

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

    // Helper: create a bank account and return its id
    private static async Task<Guid> CreateBankAccountAsync(HttpClient client, string token, string ifscCode)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/bank-accounts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            bankName = "HDFC Bank",
            accountNumber = UniqueAccountNumber(),
            accountType = "Savings",
            ifscCode = ifscCode,
            accountHolderName = "Test Owner"
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    // ======================= EXISTING TESTS (PRESERVED) =======================

    [Fact]
    public async Task GetBankAccounts_WithoutAuth_Returns401()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/bank-accounts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AddBankAccount_Returns200_AndListReturnsAccount()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var addRequest = new HttpRequestMessage(HttpMethod.Post, "/api/bank-accounts");
        addRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        addRequest.Content = JsonContent.Create(new
        {
            bankName = "HDFC Bank",
            accountNumber = "12345678901234",
            accountType = "Savings",
            ifscCode = "HDFC0001234",
            accountHolderName = "Test Owner"
        });

        var addResponse = await client.SendAsync(addRequest);
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/bank-accounts");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse = await client.SendAsync(listRequest);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var body = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetArrayLength() >= 1);
        Assert.Equal("HDFC Bank", body[0].GetProperty("bankName").GetString());
        Assert.Equal("Savings", body[0].GetProperty("accountType").GetString());
    }

    [Fact]
    public async Task AddBankAccount_WithInvalidIfscCode_Returns400()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/bank-accounts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            bankName = "HDFC Bank",
            accountNumber = "12345678901234",
            accountType = "Savings",
            ifscCode = "INVALID-IFSC",
            accountHolderName = "Test Owner"
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddBankAccount_WithInvalidAccountType_Returns400()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/bank-accounts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            bankName = "HDFC Bank",
            accountNumber = "12345678901234",
            accountType = "InvalidType",
            ifscCode = "HDFC0001234",
            accountHolderName = "Test Owner"
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ======================= NEW: UPDATE BANK ACCOUNT =======================

    [Fact]
    public async Task UpdateBankAccount_UpdatesFieldsAndReturns200()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        var accountId = await CreateBankAccountAsync(client, ownerToken, UniqueIfsc());

        using var updateRequest = new HttpRequestMessage(
            HttpMethod.Put, $"/api/bank-accounts/{accountId}");
        updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        updateRequest.Content = JsonContent.Create(new
        {
            bankName = "ICICI Bank",
            accountNumber = "98765432109876",
            accountType = "Current",
            ifscCode = "ICIC0005678",
            accountHolderName = "Updated Owner"
        });

        var updateResponse = await client.SendAsync(updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/bank-accounts");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse = await client.SendAsync(listRequest);
        var accounts = await listResponse.Content.ReadFromJsonAsync<JsonElement>();

        var updated = accounts.EnumerateArray().First(a => a.GetProperty("id").GetGuid() == accountId);
        Assert.Equal("ICICI Bank", updated.GetProperty("bankName").GetString());
        Assert.Equal("Current", updated.GetProperty("accountType").GetString());
        Assert.Equal("Updated Owner", updated.GetProperty("accountHolderName").GetString());
    }

    [Fact]
    public async Task UpdateBankAccount_NonExistent_ReturnsNotFound()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(
            HttpMethod.Put, $"/api/bank-accounts/{Guid.NewGuid()}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            bankName = "HDFC Bank",
            accountNumber = "12345678901234",
            accountType = "Savings",
            ifscCode = "HDFC0001234",
            accountHolderName = "Owner"
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateBankAccount_OtherShop_ReturnsNotFound()
    {
        using var client = CreateClient();
        var tokenA = await RegisterAsync(client);
        var (_, ownerTokenA) = await CreateShopAsync(client, tokenA);

        var accountId = await CreateBankAccountAsync(client, ownerTokenA, UniqueIfsc());

        var tokenB = await RegisterAsync(client);
        var (_, ownerTokenB) = await CreateShopAsync(client, tokenB);

        using var request = new HttpRequestMessage(
            HttpMethod.Put, $"/api/bank-accounts/{accountId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);
        request.Content = JsonContent.Create(new
        {
            bankName = "HDFC Bank",
            accountNumber = "12345678901234",
            accountType = "Savings",
            ifscCode = "HDFC0001234",
            accountHolderName = "Intruder"
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ======================= NEW: DELETE BANK ACCOUNT =======================

    [Fact]
    public async Task DeleteBankAccount_ReturnsNoContent()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        var accountId = await CreateBankAccountAsync(client, ownerToken, UniqueIfsc());

        using var deleteRequest = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/bank-accounts/{accountId}");
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var deleteResponse = await client.SendAsync(deleteRequest);

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/bank-accounts");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse = await client.SendAsync(listRequest);
        var accounts = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(accounts.EnumerateArray(), a => a.GetProperty("id").GetGuid() == accountId);
    }

    [Fact]
    public async Task DeleteBankAccount_NonExistent_ReturnsNotFound()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/bank-accounts/{Guid.NewGuid()}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteBankAccount_OtherShop_ReturnsNotFound()
    {
        using var client = CreateClient();
        var tokenA = await RegisterAsync(client);
        var (_, ownerTokenA) = await CreateShopAsync(client, tokenA);

        var accountId = await CreateBankAccountAsync(client, ownerTokenA, UniqueIfsc());

        var tokenB = await RegisterAsync(client);
        var (_, ownerTokenB) = await CreateShopAsync(client, tokenB);

        using var request = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/bank-accounts/{accountId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}