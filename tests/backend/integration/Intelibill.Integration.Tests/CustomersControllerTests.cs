using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/customers");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse = await client.SendAsync(listRequest);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var body = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetArrayLength() >= 1);
        Assert.Equal("John Doe", body[0].GetProperty("name").GetString());
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
            isActive = false
        });
        
        var editResponse = await client.SendAsync(editRequest);
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);

        using var verifyRequest = new HttpRequestMessage(HttpMethod.Get, "/api/customers");
        verifyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var verifyResponse = await client.SendAsync(verifyRequest);
        var verifiedCustomers = await verifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        
        Assert.True(verifiedCustomers.GetArrayLength() >= 1);
        Assert.Equal("John Doe Updated", verifiedCustomers[0].GetProperty("name").GetString());
        Assert.Equal("+918888888888", verifiedCustomers[0].GetProperty("phoneNumber").GetString());
        Assert.False(verifiedCustomers[0].GetProperty("isActive").GetBoolean());
    }
}
