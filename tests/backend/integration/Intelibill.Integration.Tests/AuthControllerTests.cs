using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
    public async Task ConfirmedEmailEnrollment_Returns200WithTokens()
    {
        using var client = CreateClient();

        var response = await client.EnrollAsync(new
        {
            email = UniqueEmail(),
            password = "Pass123!Aa",
            firstName = "Test",
            lastName = "User",
            phoneNumber = UniquePhone()
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("refreshToken").GetString()));
        Assert.Equal("Test", body.GetProperty("user").GetProperty("firstName").GetString());
    }

    [Fact]
    public async Task Registration_ExistingEmail_ReturnsGenericAccepted()
    {
        using var client = CreateClient();
        var email = UniqueEmail();
        await client.EnrollAsync(new { email, password = "Pass123!Aa", firstName = "Test", lastName = "User" });
        var response = await client.PostAsJsonAsync("/api/auth/enrollment/email/request", new { email });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.DoesNotContain("accessToken", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The route that used to hand out a session on an unproved number is closed. It still answers,
    /// with a stable code, so a client that has not been updated gets a documented refusal rather
    /// than a 404 or a 500 — and no account is created.
    /// </summary>
    [Fact]
    public async Task RegisterWithPhone_IsClosed_Returns503AndCreatesNothing()
    {
        using var client = CreateClient();
        var phone = UniquePhone();

        var response = await client.PostAsJsonAsync("/api/auth/register/phone", new
        {
            phoneNumber = phone,
            firstName = "Phone",
            lastName = "User"
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Auth.PhoneEnrollmentUnavailable", body);
        Assert.DoesNotContain("accessToken", body);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Users.AnyAsync(x => x.PhoneNumber == phone));
    }

    [Fact]
    public async Task LoginWithEmail_ValidCredentials_Returns200WithTokens()
    {
        using var client = CreateClient();
        var email = UniqueEmail();
        await client.EnrollAsync(new
        {
            email, password = "Pass123!Aa", firstName = "Test", lastName = "User",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}"
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
        await client.EnrollAsync(new
        {
            email, password = "Pass123!Aa", firstName = "Test", lastName = "User",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}"
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

        var registration = await client.EnrollAsync(new { email, password, firstName = "Phone", lastName = "Owner" });
        var authentication = await registration.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authentication.GetProperty("accessToken").GetString());
        var profile = await client.PutAsJsonAsync("/api/users/me", new { email, phoneNumber = phone, firstName = "Phone", lastName = "Owner", language = "en-IN" });
        profile.EnsureSuccessStatusCode();

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

        var registration = await client.EnrollAsync(new { email, password, firstName = "Phone", lastName = "Owner" });
        var authentication = await registration.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authentication.GetProperty("accessToken").GetString());
        var profile = await client.PutAsJsonAsync("/api/users/me", new { email, phoneNumber = phone, firstName = "Phone", lastName = "Owner", language = "en-IN" });
        profile.EnsureSuccessStatusCode();

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
        var registerResponse = await client.EnrollAsync(new
        {
            email = UniqueEmail(), password = "Pass123!Aa", firstName = "Test", lastName = "User",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}"
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
        var registerResponse = await client.EnrollAsync(new
        {
            email = UniqueEmail(), password = "Pass123!Aa", firstName = "Test", lastName = "User",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}"
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
        var registerResponse = await client.EnrollAsync(new
        {
            email = UniqueEmail(), password = "Pass123!Aa", firstName = "Test", lastName = "User",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}"
        });
        var body = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var refreshToken = body.GetProperty("refreshToken").GetString()!;

        await client.PostAsJsonAsync("/api/auth/token/revoke", new { refreshToken });
        var response = await client.PostAsJsonAsync("/api/auth/token/refresh", new { refreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/auth/login/external")]
    [InlineData("/api/auth/login/external/init")]
    [InlineData("/api/auth/login/external/callback")]
    public async Task ExternalLoginEndpoints_AreGone(string path)
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(path, new { provider = 1, token = "token", code = "code", state = "state" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/auth/google/callback?code=123")]
    [InlineData("/auth/facebook/callback?code=456")]
    public async Task ProviderCallbackRelays_AreGone(string path)
    {
        using var client = CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }
}
