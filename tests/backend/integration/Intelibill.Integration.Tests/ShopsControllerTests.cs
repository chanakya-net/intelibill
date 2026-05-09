using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class ShopsControllerTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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
        AllowAutoRedirect = false
    });

    private static string UniqueEmail() => $"shops-{Guid.NewGuid():N}@test.com";

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email = UniqueEmail(),
            password = "Pass123!Aa",
            firstName = "Test",
            lastName = "User"
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static async Task<(Guid ShopId, string OwnerToken)> CreateShopAsync(HttpClient client, string token, string? name = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name = name ?? $"Shop {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
            contactPerson = (string?)null,
            mobileNumber = (string?)null,
            gstNumber = (string?)null
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("activeShopId").GetGuid(), body.GetProperty("accessToken").GetString()!);
    }

    // ── GET /api/shops/me ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyShops_WithoutAuth_Returns401()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/shops/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyShops_WithAuth_ReturnsOwnedShops()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/shops/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetArrayLength() >= 1);
        Assert.True(body[0].GetProperty("isDefault").GetBoolean());
    }

    // ── GET /api/shops/{shopId} ──────────────────────────────────────────────────

    [Fact]
    public async Task GetShopDetails_ForMemberShop_Returns200WithDetails()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (shopId, ownerToken) = await CreateShopAsync(client, token, "Detail Shop");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/shops/{shopId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(shopId.ToString(), body.GetProperty("shopId").GetString());
        Assert.Equal("Detail Shop", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetShopDetails_ForNonMemberShop_Returns403()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/shops/{Guid.NewGuid()}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── POST /api/shops ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateShop_WithoutAuth_Returns401()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/shops", new
        {
            name = "Test Shop", address = "Address", city = "City", state = "State", pincode = "560001"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateShop_WithValidData_Returns200WithNewOwnerToken()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name = "My New Shop",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
            contactPerson = "Owner",
            mobileNumber = "9876543210",
            gstNumber = "27AAPFU0939F1ZV"
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.NotNull(body.GetProperty("activeShopId").GetString());
        var shop = body.GetProperty("shops")[0];
        Assert.Equal("My New Shop", shop.GetProperty("shopName").GetString());
        Assert.True(shop.GetProperty("isDefault").GetBoolean());
    }

    // ── POST /api/shops/switch ───────────────────────────────────────────────────

    [Fact]
    public async Task SwitchActiveShop_WithoutAuth_Returns401()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/shops/switch", new { shopId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SwitchActiveShop_WithoutOwnerClaim_Returns403()
    {
        using var client = CreateClient();
        // Token from registration has no active_shop_role = Owner claim
        var noShopToken = await RegisterAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops/switch");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", noShopToken);
        request.Content = JsonContent.Create(new { shopId = Guid.NewGuid() });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SwitchActiveShop_AsOwnerToSecondShop_Returns200WithNewToken()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (shopId1, ownerToken1) = await CreateShopAsync(client, token);
        var (shopId2, ownerToken2) = await CreateShopAsync(client, ownerToken1);

        // Switch to shopId2 using ownerToken2 (active shop is shop1, the default)
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops/switch");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken2);
        request.Content = JsonContent.Create(new { shopId = shopId2 });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(shopId2.ToString(), body.GetProperty("activeShopId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task SwitchActiveShop_ToNonMemberShop_Returns403()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops/switch");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new { shopId = Guid.NewGuid() });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── POST /api/shops/default ──────────────────────────────────────────────────

    [Fact]
    public async Task SetDefaultShop_WithValidMembership_Returns200()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (shopId1, ownerToken1) = await CreateShopAsync(client, token);
        var (shopId2, ownerToken2) = await CreateShopAsync(client, ownerToken1);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops/default");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken2);
        request.Content = JsonContent.Create(new { shopId = shopId2 });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(shopId2.ToString(), body.GetProperty("activeShopId").GetString());
    }

    [Fact]
    public async Task SetDefaultShop_ForNonMemberShop_Returns403()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops/default");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new { shopId = Guid.NewGuid() });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── GET /api/shops/me — multi-shop ──────────────────────────────────────────

    [Fact]
    public async Task GetMyShops_AfterCreatingTwoShops_ReturnsBothShops()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken1) = await CreateShopAsync(client, token, "Shop Alpha");
        var (_, ownerToken2) = await CreateShopAsync(client, ownerToken1, "Shop Beta");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/shops/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken2);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetArrayLength() >= 2);
    }

    // ── POST /api/shops — validation ─────────────────────────────────────────────

    [Fact]
    public async Task CreateShop_WithEmptyName_Returns400()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name = "",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001"
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateShop_WithInvalidGstNumber_Returns400()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name = "GST Test Shop",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
            gstNumber = "INVALID-GST"
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── PUT /api/shops/{shopId} ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateShop_WithoutOwnerClaim_Returns403()
    {
        using var client = CreateClient();
        var noShopToken = await RegisterAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/shops/{Guid.NewGuid()}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", noShopToken);
        request.Content = JsonContent.Create(new
        {
            name = "Updated", address = "Addr", city = "City", state = "State", pincode = "560001"
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateShop_AsOwner_Returns200WithUpdatedDetails()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (shopId, ownerToken) = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/shops/{shopId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            name = "Updated Shop Name",
            address = "New Address",
            city = "Mumbai",
            state = "Maharashtra",
            pincode = "400001",
            contactPerson = "Updated Contact",
            mobileNumber = "9876543210",
            gstNumber = (string?)null
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Updated Shop Name", body.GetProperty("name").GetString());
        Assert.Equal("Mumbai", body.GetProperty("city").GetString());
        Assert.Equal("Maharashtra", body.GetProperty("state").GetString());
    }

    [Fact]
    public async Task UpdateShop_ForNonMemberShop_Returns403()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        // Try to update a shop the user is not owner of
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/shops/{Guid.NewGuid()}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            name = "Updated", address = "Addr", city = "City", state = "State", pincode = "560001"
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateShop_WithEmptyRequiredFields_Returns400()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (shopId, ownerToken) = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/shops/{shopId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            name = "",
            address = "",
            city = "Mumbai",
            state = "Maharashtra",
            pincode = "400001"
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── POST /api/shops/default — isDefault flag ──────────────────────────────────

    [Fact]
    public async Task SetDefaultShop_SecondShop_UpdatesIsDefaultFlag()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (shopId1, ownerToken1) = await CreateShopAsync(client, token, "First Shop");
        var (shopId2, ownerToken2) = await CreateShopAsync(client, ownerToken1, "Second Shop");

        // Set shop2 as default
        using var setDefault = new HttpRequestMessage(HttpMethod.Post, "/api/shops/default");
        setDefault.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken2);
        setDefault.Content = JsonContent.Create(new { shopId = shopId2 });
        var setResponse = await client.SendAsync(setDefault);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);
        var newToken = (await setResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        // Verify shop2 is now default in /me
        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/shops/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
        var meResponse = await client.SendAsync(meRequest);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var shops = await meResponse.Content.ReadFromJsonAsync<JsonElement>();

        var shop2Entry = Enumerable.Range(0, shops.GetArrayLength())
            .Select(i => shops[i])
            .FirstOrDefault(s => s.GetProperty("shopId").GetString() == shopId2.ToString());
        Assert.True(shop2Entry.GetProperty("isDefault").GetBoolean());

        var shop1Entry = Enumerable.Range(0, shops.GetArrayLength())
            .Select(i => shops[i])
            .FirstOrDefault(s => s.GetProperty("shopId").GetString() == shopId1.ToString());
        Assert.False(shop1Entry.GetProperty("isDefault").GetBoolean());
    }

}
