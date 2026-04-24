using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelibill.Integration.Tests;

public class BankAccountsControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public BankAccountsControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static string UniqueEmail() => $"bank-{Guid.NewGuid():N}@test.com";

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
}
