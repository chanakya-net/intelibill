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
}