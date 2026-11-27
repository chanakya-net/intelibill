using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class UsersControllerTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    private static string UniqueEmail() => $"users-{Guid.NewGuid():N}@test.com";

    private static async Task<(string AccessToken, string Email)> RegisterAsync(HttpClient client)
    {
        var email = UniqueEmail();
        var response = await client.EnrollAsync(new
        {
            email,
            password = "Pass123!Aa",
            firstName = "Test",
            lastName = "User",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}"
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
    public async Task UpdateMyProfile_WithValidData_Returns200WithTheSavedProfile()
    {
        using var client = CreateClient();
        var (token, email) = await RegisterAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            email,
            phoneNumber = (string?)null,
            firstName = "Updated",
            lastName = "Profile",
            language = "en-IN"
        });
        var response = await client.SendAsync(request);

        // The saved profile comes back on its own: editing a name is not a credential event, so the
        // response carries no token and the session it arrived on carries on unchanged.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", payload, StringComparison.OrdinalIgnoreCase);
        var body = JsonDocument.Parse(payload).RootElement;
        Assert.Equal("Updated", body.GetProperty("firstName").GetString());
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
            currentPassword = "Pass123!Aa", newPassword = "NewPass456!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangeMyPassword_WithCorrectCurrentPassword_Returns200()
    {
        using var client = CreateClient();
        var email = UniqueEmail();
        var registerResponse = await client.EnrollAsync(new
        {
            email, password = "Pass123!Aa", firstName = "Test", lastName = "User",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}"
        });
        var body = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("accessToken").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users/me/change-password");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            currentPassword = "Pass123!Aa",
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
        var registerResponse = await client.EnrollAsync(new
        {
            email, password = "Pass123!Aa", firstName = "Test", lastName = "User",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}"
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
