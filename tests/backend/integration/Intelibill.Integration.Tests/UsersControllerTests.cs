using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelibill.Integration.Tests;

public class UsersControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public UsersControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    private static string UniqueEmail() => $"users-{Guid.NewGuid():N}@test.com";
    private static string UniquePhone() => $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}";

    private static async Task<(string AccessToken, string Email)> RegisterAsync(HttpClient client)
    {
        var email = UniqueEmail();
        var response = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email,
            password = "Pass123!",
            firstName = "Test",
            lastName = "User"
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("accessToken").GetString()!, email);
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
            pincode = "560001"
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("activeShopId").GetGuid(), body.GetProperty("accessToken").GetString()!);
    }

    private static async Task<(Guid UserId, string Email)> AddShopUserAsync(
        HttpClient client, string ownerToken, Guid shopId, string role = "Manager")
    {
        var email = UniqueEmail();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            shopIds = new[] { shopId },
            email,
            firstName = "Staff",
            lastName = "Member",
            phoneNumber = UniquePhone(),
            password = "StaffPass1!",
            confirmPassword = "StaffPass1!",
            role
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("userId").GetGuid(), email);
    }

    // ── GET /api/users ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetShopUsers_WithoutAuth_Returns401()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetShopUsers_WithoutActiveShopInToken_Returns400()
    {
        using var client = CreateClient();
        // Fresh registration token has no active_shop_id claim
        var (token, _) = await RegisterAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetShopUsers_WithActiveShop_Returns200WithOwnerInList()
    {
        using var client = CreateClient();
        var (token, _) = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetArrayLength() >= 1);
        Assert.Equal("Owner", body[0].GetProperty("role").GetString());
    }

    // ── POST /api/users ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AddShopUser_WithoutOwnerClaim_Returns403()
    {
        using var client = CreateClient();
        var (noShopToken, _) = await RegisterAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", noShopToken);
        request.Content = JsonContent.Create(new
        {
            shopIds = new[] { Guid.NewGuid() },
            email = UniqueEmail(),
            firstName = "Staff",
            lastName = "Member",
            phoneNumber = UniquePhone(),
            password = "StaffPass1!",
            confirmPassword = "StaffPass1!",
            role = "Manager"
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AddShopUser_AsOwner_WithManagerRole_Returns201()
    {
        using var client = CreateClient();
        var (token, _) = await RegisterAsync(client);
        var (shopId, ownerToken) = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            shopIds = new[] { shopId },
            email = UniqueEmail(),
            firstName = "New",
            lastName = "Manager",
            phoneNumber = UniquePhone(),
            password = "StaffPass1!",
            confirmPassword = "StaffPass1!",
            role = "Manager"
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("New", body.GetProperty("firstName").GetString());
        Assert.Equal("Manager", body.GetProperty("role").GetString());
    }

    [Fact]
    public async Task AddShopUser_AsOwner_WithStaffRole_Returns201()
    {
        using var client = CreateClient();
        var (token, _) = await RegisterAsync(client);
        var (shopId, ownerToken) = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            shopIds = new[] { shopId },
            email = UniqueEmail(),
            firstName = "New",
            lastName = "Staff",
            phoneNumber = UniquePhone(),
            password = "StaffPass1!",
            confirmPassword = "StaffPass1!",
            role = "Staff"
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Staff", body.GetProperty("role").GetString());
    }

    [Fact]
    public async Task AddShopUser_WithInvalidRole_Returns400()
    {
        using var client = CreateClient();
        var (token, _) = await RegisterAsync(client);
        var (shopId, ownerToken) = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            shopIds = new[] { shopId },
            email = UniqueEmail(),
            firstName = "Test",
            lastName = "User",
            phoneNumber = UniquePhone(),
            password = "Pass123!",
            confirmPassword = "Pass123!",
            role = "Owner" // Cannot assign Owner role
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddShopUser_WithDuplicateEmail_Returns409()
    {
        using var client = CreateClient();
        var (token, _) = await RegisterAsync(client);
        var (shopId, ownerToken) = await CreateShopAsync(client, token);
        var duplicateEmail = UniqueEmail();

        using var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        request1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request1.Content = JsonContent.Create(new
        {
            shopIds = new[] { shopId },
            email = duplicateEmail,
            firstName = "First",
            lastName = "User",
            phoneNumber = UniquePhone(),
            password = "Pass123!",
            confirmPassword = "Pass123!",
            role = "Manager"
        });
        await client.SendAsync(request1);

        using var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        request2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request2.Content = JsonContent.Create(new
        {
            shopIds = new[] { shopId },
            email = duplicateEmail,
            firstName = "Second",
            lastName = "User",
            phoneNumber = UniquePhone(),
            password = "Pass123!",
            confirmPassword = "Pass123!",
            role = "Manager"
        });
        var response = await client.SendAsync(request2);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── PUT /api/users/{targetUserId} ────────────────────────────────────────────

    [Fact]
    public async Task EditShopUser_WithoutOwnerClaim_Returns403()
    {
        using var client = CreateClient();
        var (noShopToken, _) = await RegisterAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{Guid.NewGuid()}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", noShopToken);
        request.Content = JsonContent.Create(new
        {
            email = UniqueEmail(),
            firstName = "Test",
            lastName = "User",
            phoneNumber = UniquePhone(),
            role = "Manager",
            isLoginEnabled = true
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EditShopUser_AsOwner_Returns200WithUpdatedData()
    {
        using var client = CreateClient();
        var (token, _) = await RegisterAsync(client);
        var (shopId, ownerToken) = await CreateShopAsync(client, token);
        var (targetUserId, _) = await AddShopUserAsync(client, ownerToken, shopId);

        using var editRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{targetUserId}");
        editRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        editRequest.Content = JsonContent.Create(new
        {
            email = UniqueEmail(),
            firstName = "Updated",
            lastName = "Name",
            phoneNumber = UniquePhone(),
            role = "Staff",
            isLoginEnabled = true
        });
        var response = await client.SendAsync(editRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Updated", body.GetProperty("firstName").GetString());
        Assert.Equal("Staff", body.GetProperty("role").GetString());
    }

    [Fact]
    public async Task EditShopUser_TargetNotInShop_Returns404()
    {
        using var client = CreateClient();
        var (token, _) = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{Guid.NewGuid()}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            email = UniqueEmail(),
            firstName = "Test",
            lastName = "User",
            phoneNumber = UniquePhone(),
            role = "Manager",
            isLoginEnabled = true
        });
        var response = await client.SendAsync(request);

        // User not found in the shop → 404
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── PUT /api/users/me ────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateMyProfile_WithoutAuth_Returns401()
    {
        using var client = CreateClient();

        var response = await client.PutAsJsonAsync("/api/users/me", new
        {
            email = UniqueEmail(), phoneNumber = (string?)null, firstName = "Test", lastName = "User", language = "en-IN"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMyProfile_WithValidData_Returns200WithNewToken()
    {
        using var client = CreateClient();
        var (token, _) = await RegisterAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            email = UniqueEmail(),
            phoneNumber = (string?)null,
            firstName = "Updated",
            lastName = "Profile",
            language = "en-IN"
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.Equal("Updated", body.GetProperty("user").GetProperty("firstName").GetString());
    }

    [Fact]
    public async Task UpdateMyProfile_WithEmailTakenByOtherUser_Returns409()
    {
        using var client = CreateClient();

        // Register user A
        var (_, existingEmail) = await RegisterAsync(client);

        // Register user B and try to claim user A's email
        var (tokenB, _) = await RegisterAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        request.Content = JsonContent.Create(new
        {
            email = existingEmail,
            phoneNumber = (string?)null,
            firstName = "Test",
            lastName = "User",
            language = "en-IN"
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── POST /api/users/me/change-password ───────────────────────────────────────

    [Fact]
    public async Task ChangeMyPassword_WithoutAuth_Returns401()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/users/me/change-password", new
        {
            currentPassword = "Pass123!", newPassword = "NewPass456!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangeMyPassword_WithCorrectCurrentPassword_Returns200()
    {
        using var client = CreateClient();
        var email = UniqueEmail();
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email, password = "Pass123!", firstName = "Test", lastName = "User"
        });
        var body = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("accessToken").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users/me/change-password");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            currentPassword = "Pass123!",
            newPassword = "NewPass456!"
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("changed", responseBody.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangeMyPassword_WithIncorrectCurrentPassword_Returns401()
    {
        using var client = CreateClient();
        var email = UniqueEmail();
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email, password = "Pass123!", firstName = "Test", lastName = "User"
        });
        var body = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("accessToken").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users/me/change-password");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            currentPassword = "WrongCurrentPassword!",
            newPassword = "NewPass456!"
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
