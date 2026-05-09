using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class AuthControllerTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    private static string UniqueEmail() => $"auth-{Guid.NewGuid():N}@test.com";
    private static string UniquePhone() => $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}";

    [Fact]
    public async Task RegisterWithEmail_ValidRequest_Returns201WithTokens()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email = UniqueEmail(),
            password = "Pass123!Aa",
            firstName = "Test",
            lastName = "User",
            phoneNumber = UniquePhone()
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("refreshToken").GetString()));
        Assert.Equal("Test", body.GetProperty("user").GetProperty("firstName").GetString());
    }

    [Fact]
    public async Task RegisterWithEmail_DuplicateEmail_Returns409()
    {
        using var client = CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email, password = "Pass123!Aa", firstName = "Test", lastName = "User"
        });

        var response = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email, password = "Pass123!Aa", firstName = "Another", lastName = "User"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RegisterWithPhone_ValidRequest_Returns201WithTokens()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register/phone", new
        {
            phoneNumber = UniquePhone(),
            firstName = "Phone",
            lastName = "User"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("refreshToken").GetString()));
    }

    [Fact]
    public async Task RegisterWithPhone_DuplicatePhone_Returns409()
    {
        using var client = CreateClient();
        var phone = UniquePhone();
        await client.PostAsJsonAsync("/api/auth/register/phone", new
        {
            phoneNumber = phone, firstName = "First", lastName = "User"
        });

        var response = await client.PostAsJsonAsync("/api/auth/register/phone", new
        {
            phoneNumber = phone, firstName = "Second", lastName = "User"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task LoginWithEmail_ValidCredentials_Returns200WithTokens()
    {
        using var client = CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email, password = "Pass123!Aa", firstName = "Test", lastName = "User"
        });

        var response = await client.PostAsJsonAsync("/api/auth/login/email", new
        {
            email, password = "Pass123!Aa"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("refreshToken").GetString()));
    }

    [Fact]
    public async Task LoginWithEmail_WrongPassword_Returns401()
    {
        using var client = CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email, password = "Pass123!Aa", firstName = "Test", lastName = "User"
        });

        var response = await client.PostAsJsonAsync("/api/auth/login/email", new
        {
            email, password = "WrongPassword!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginWithEmail_UnknownEmail_Returns401()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login/email", new
        {
            email = UniqueEmail(),
            password = "Pass123!Aa"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginWithPhone_ExactStoredPhoneAndPassword_Returns200()
    {
        using var client = CreateClient();
        var phone = UniquePhone();
        var password = "StaffPass1!";
        var email = UniqueEmail();

        var ownerRegister = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email = UniqueEmail(),
            password = "Pass123!Aa",
            firstName = "Owner",
            lastName = "User",
            phoneNumber = UniquePhone()
        });
        ownerRegister.EnsureSuccessStatusCode();
        var ownerBody = await ownerRegister.Content.ReadFromJsonAsync<JsonElement>();
        var ownerToken = ownerBody.GetProperty("accessToken").GetString()!;

        using var createShopRequest = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        createShopRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ownerToken);
        createShopRequest.Content = JsonContent.Create(new
        {
            name = $"Shop {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001"
        });
        var createShopResponse = await client.SendAsync(createShopRequest);
        createShopResponse.EnsureSuccessStatusCode();
        var shopBody = await createShopResponse.Content.ReadFromJsonAsync<JsonElement>();
        var shopId = shopBody.GetProperty("activeShopId").GetGuid();
        var scopedOwnerToken = shopBody.GetProperty("accessToken").GetString()!;

        using var addUserRequest = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        addUserRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", scopedOwnerToken);
        addUserRequest.Content = JsonContent.Create(new
        {
            shopIds = new[] { shopId },
            email,
            firstName = "Staff",
            lastName = "Member",
            phoneNumber = phone,
            password,
            confirmPassword = password,
            role = "Manager"
        });
        var addUserResponse = await client.SendAsync(addUserRequest);
        addUserResponse.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            identifier = $"  {phone}  ",
            password
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(phone, body.GetProperty("user").GetProperty("phoneNumber").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task LoginWithPhone_MismatchOrWrongPassword_Returns401()
    {
        using var client = CreateClient();
        var phone = UniquePhone();
        var password = "StaffPass1!";
        var email = UniqueEmail();

        var ownerRegister = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email = UniqueEmail(),
            password = "Pass123!Aa",
            firstName = "Owner",
            lastName = "User",
            phoneNumber = UniquePhone()
        });
        ownerRegister.EnsureSuccessStatusCode();
        var ownerBody = await ownerRegister.Content.ReadFromJsonAsync<JsonElement>();
        var ownerToken = ownerBody.GetProperty("accessToken").GetString()!;

        using var createShopRequest = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        createShopRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ownerToken);
        createShopRequest.Content = JsonContent.Create(new
        {
            name = $"Shop {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001"
        });
        var createShopResponse = await client.SendAsync(createShopRequest);
        createShopResponse.EnsureSuccessStatusCode();
        var shopBody = await createShopResponse.Content.ReadFromJsonAsync<JsonElement>();
        var shopId = shopBody.GetProperty("activeShopId").GetGuid();
        var scopedOwnerToken = shopBody.GetProperty("accessToken").GetString()!;

        using var addUserRequest = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        addUserRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", scopedOwnerToken);
        addUserRequest.Content = JsonContent.Create(new
        {
            shopIds = new[] { shopId },
            email,
            firstName = "Staff",
            lastName = "Member",
            phoneNumber = phone,
            password,
            confirmPassword = password,
            role = "Manager"
        });
        var addUserResponse = await client.SendAsync(addUserRequest);
        addUserResponse.EnsureSuccessStatusCode();

        var mismatchResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            identifier = phone.TrimStart('+'),
            password
        });
        Assert.Equal(HttpStatusCode.Unauthorized, mismatchResponse.StatusCode);

        var wrongPasswordResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            identifier = phone,
            password = "WrongPass!"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPasswordResponse.StatusCode);
    }

    [Fact]
    public async Task RequestPasswordReset_ForAnyEmail_AlwaysReturns200()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/password-reset/request", new
        {
            email = UniqueEmail()
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("reset link", body.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResetPassword_WithInvalidToken_Returns401()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/password-reset/confirm", new
        {
            email = UniqueEmail(),
            token = "bogus-reset-token",
            newPassword = "NewPass123!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RefreshToken_WithValidToken_Returns200WithRotatedToken()
    {
        using var client = CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email = UniqueEmail(), password = "Pass123!Aa", firstName = "Test", lastName = "User"
        });
        var registerBody = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var originalRefreshToken = registerBody.GetProperty("refreshToken").GetString()!;

        var response = await client.PostAsJsonAsync("/api/auth/token/refresh", new
        {
            refreshToken = originalRefreshToken
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.NotEqual(originalRefreshToken, body.GetProperty("refreshToken").GetString());
    }

    [Fact]
    public async Task RefreshToken_WithInvalidToken_Returns401()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token/refresh", new
        {
            refreshToken = "invalid-refresh-token"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RevokeToken_WithValidToken_Returns204()
    {
        using var client = CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email = UniqueEmail(), password = "Pass123!Aa", firstName = "Test", lastName = "User"
        });
        var body = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var refreshToken = body.GetProperty("refreshToken").GetString()!;

        var response = await client.PostAsJsonAsync("/api/auth/token/revoke", new { refreshToken });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RevokeToken_ThenRefresh_Returns401()
    {
        using var client = CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email = UniqueEmail(), password = "Pass123!Aa", firstName = "Test", lastName = "User"
        });
        var body = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var refreshToken = body.GetProperty("refreshToken").GetString()!;

        await client.PostAsJsonAsync("/api/auth/token/revoke", new { refreshToken });
        var response = await client.PostAsJsonAsync("/api/auth/token/refresh", new { refreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InitializeExternalLogin_WhenProviderDisabled_Returns400()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login/external/init", new
        {
            provider = 1 // Google
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CompleteExternalLogin_WhenStateIsInvalid_Returns401()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login/external/callback", new
        {
            code = "code-123",
            state = "invalid-state"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GoogleCallbackRelay_RedirectsToFrontend()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/auth/google/callback?code=123");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/auth/callback?code=123", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task FacebookCallbackRelay_RedirectsToFrontend()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/auth/facebook/callback?code=456");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/auth/callback?code=456", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task ExternalLogin_WithInvalidProviderToken_ReturnsError()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login/external", new
        {
            provider = 1, // Google
            token = "invalid-token"
        });

        Assert.False(response.IsSuccessStatusCode);
    }
}
