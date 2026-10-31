using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Intelibill.Integration.Tests;

/// <summary>
/// A session belongs to one device. These tests pin the three things that follows from that: an
/// ordinary profile or shop operation reuses the session it arrived on instead of minting another
/// refresh credential, a user can see and revoke their own devices one at a time, and a security
/// change ends every device at once.
/// </summary>
[Collection("Integration Tests")]
public sealed class SessionLifecycleIntegrationTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string Password = "OwnersPassword!1";

    private readonly ApiWebApplicationFactory _factory = new(fixture, services =>
    {
        services.RemoveAll<IEmailService>();
        services.AddSingleton<ResetLinkCapture>();
        services.AddSingleton<IEmailService>(sp => sp.GetRequiredService<ResetLinkCapture>());
    });

    public Task InitializeAsync() => _factory.InitializeAsync();

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

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static string Email() => $"owner-{Guid.NewGuid():N}@test.com";

    private async Task<JsonElement> EnrollAsync(HttpClient client, string email)
    {
        var response = await _factory.EnrollAsync(client, new
        {
            email,
            password = Password,
            firstName = "Mailbox",
            lastName = "Owner",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { identifier = email, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static void Authenticate(HttpClient client, string accessToken) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string accessToken,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<(Guid ShopId, string AccessToken)> CreateShopAsync(HttpClient client, string accessToken)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/api/shops", accessToken, new
        {
            name = $"Shop {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("activeShopId").GetGuid(), body.GetProperty("accessToken").GetString()!);
    }

    private async Task<int> ActiveRefreshCountAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.RefreshTokens.CountAsync(x => x.UserId == userId && !x.IsRevoked);
    }

    private static object ProfileSave(string email, string firstName) => new
    {
        email,
        phoneNumber = (string?)null,
        firstName,
        lastName = "Owner",
        language = "en-IN",
    };

    // ── Ordinary operations reuse the session they arrived on ────────────────────

    [Fact]
    public async Task ProfileSave_DoesNotMintAnotherRefreshCredential()
    {
        using var client = Client();
        var email = Email();
        var auth = await EnrollAsync(client, email);
        var userId = auth.GetProperty("user").GetProperty("id").GetGuid();
        var (_, scopedToken) = await CreateShopAsync(client, auth.GetProperty("accessToken").GetString()!);

        var before = await ActiveRefreshCountAsync(userId);

        var response = await SendAsync(client, HttpMethod.Put, "/api/users/me", scopedToken, ProfileSave(email, "Renamed"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("refreshToken", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await ActiveRefreshCountAsync(userId));
    }

    [Fact]
    public async Task ShopSwitch_DoesNotMintAnotherRefreshCredential()
    {
        using var client = Client();
        var email = Email();
        var auth = await EnrollAsync(client, email);
        var userId = auth.GetProperty("user").GetProperty("id").GetGuid();
        var (firstShop, afterFirst) = await CreateShopAsync(client, auth.GetProperty("accessToken").GetString()!);
        var (_, afterSecond) = await CreateShopAsync(client, afterFirst);

        var before = await ActiveRefreshCountAsync(userId);

        var response = await SendAsync(client, HttpMethod.Post, "/api/shops/switch", afterSecond, new { shopId = firstShop });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("refreshToken", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await ActiveRefreshCountAsync(userId));
    }

    /// <summary>
    /// Choosing which shop opens on the next sign-in is a stored preference, not a scope change:
    /// the device that saved it keeps working in the shop it was already in.
    /// </summary>
    [Fact]
    public async Task SetDefaultShop_KeepsRequestingSessionScopeAndMintsNothing()
    {
        using var client = Client();
        var email = Email();
        var auth = await EnrollAsync(client, email);
        var userId = auth.GetProperty("user").GetProperty("id").GetGuid();
        var (firstShop, afterFirst) = await CreateShopAsync(client, auth.GetProperty("accessToken").GetString()!);
        var (secondShop, afterSecond) = await CreateShopAsync(client, afterFirst);

        var before = await ActiveRefreshCountAsync(userId);

        var response = await SendAsync(client, HttpMethod.Post, "/api/shops/default", afterSecond, new { shopId = firstShop });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(secondShop, body.GetProperty("activeShopId").GetGuid());
        Assert.True(
            body.EnumerateObject().Any(x => x.NameEquals("shops")) &&
            body.GetProperty("shops").EnumerateArray().Any(x => x.GetProperty("shopId").GetGuid() == firstShop && x.GetProperty("isDefault").GetBoolean()),
            "The saved default must come back marked as the default.");
        Assert.Equal(before, await ActiveRefreshCountAsync(userId));
    }

    // ── A user can see and revoke their own devices ──────────────────────────────

    [Fact]
    public async Task SignIn_RecordsOneSessionPerDevice()
    {
        using var deviceA = Client();
        using var deviceB = Client();
        var email = Email();
        var auth = await EnrollAsync(deviceA, email);
        var accessA = auth.GetProperty("accessToken").GetString()!;
        await LoginAsync(deviceB, email);

        var response = await SendAsync(deviceA, HttpMethod.Get, "/api/users/me/sessions", accessA);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sessions = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        Assert.Equal(2, sessions.Count);
        Assert.Single(sessions, x => x.GetProperty("isCurrent").GetBoolean());
    }

    [Fact]
    public async Task RevokeSession_EndsOnlyThatDevice()
    {
        using var deviceA = Client();
        using var deviceB = Client();
        var email = Email();
        var authA = await EnrollAsync(deviceA, email);
        var accessA = authA.GetProperty("accessToken").GetString()!;
        var authB = await LoginAsync(deviceB, email);
        var accessB = authB.GetProperty("accessToken").GetString()!;
        var refreshB = authB.GetProperty("refreshToken").GetString()!;

        var listed = await SendAsync(deviceA, HttpMethod.Get, "/api/users/me/sessions", accessA);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var others = (await listed.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray()
            .Where(x => !x.GetProperty("isCurrent").GetBoolean())
            .ToList();
        var targetSession = Assert.Single(others).GetProperty("sessionId").GetGuid();

        var revoked = await SendAsync(deviceA, HttpMethod.Delete, $"/api/users/me/sessions/{targetSession}", accessA);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(deviceB, HttpMethod.Get, "/api/shops/me", accessB)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await deviceB.PostAsJsonAsync("/api/auth/token/refresh", new { refreshToken = refreshB })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(deviceA, HttpMethod.Get, "/api/shops/me", accessA)).StatusCode);
    }

    [Fact]
    public async Task RevokeAllSessions_EndsEveryDeviceIncludingTheCaller()
    {
        using var deviceA = Client();
        using var deviceB = Client();
        var email = Email();
        var authA = await EnrollAsync(deviceA, email);
        var accessA = authA.GetProperty("accessToken").GetString()!;
        var authB = await LoginAsync(deviceB, email);
        var accessB = authB.GetProperty("accessToken").GetString()!;

        var revoked = await SendAsync(deviceA, HttpMethod.Post, "/api/users/me/sessions/revoke-all", accessA);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(deviceA, HttpMethod.Get, "/api/shops/me", accessA)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(deviceB, HttpMethod.Get, "/api/shops/me", accessB)).StatusCode);
    }

    [Fact]
    public async Task SessionList_CannotReadOrRevokeAnotherUser()
    {
        using var mine = Client();
        using var theirs = Client();
        var myEmail = Email();
        var theirEmail = Email();
        var myAuth = await EnrollAsync(mine, myEmail);
        var theirAuth = await EnrollAsync(theirs, theirEmail);
        var myAccess = myAuth.GetProperty("accessToken").GetString()!;
        var theirAccess = theirAuth.GetProperty("accessToken").GetString()!;

        var theirSessions = await SendAsync(theirs, HttpMethod.Get, "/api/users/me/sessions", theirAccess);
        Assert.Equal(HttpStatusCode.OK, theirSessions.StatusCode);
        var theirSessionId = (await theirSessions.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray()
            .First()
            .GetProperty("sessionId")
            .GetGuid();

        var attempt = await SendAsync(mine, HttpMethod.Delete, $"/api/users/me/sessions/{theirSessionId}", myAccess);
        Assert.Equal(HttpStatusCode.NotFound, attempt.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(theirs, HttpMethod.Get, "/api/shops/me", theirAccess)).StatusCode);
    }

    // ── Legacy tokens are not sessions ───────────────────────────────────────────

    /// <summary>
    /// A cryptographically valid token that names no session is refused rather than treated as a
    /// device of its own. This is the cutover rule: no session is ever invented from a legacy token.
    /// </summary>
    [Fact]
    public async Task AccessTokenWithoutSessionClaim_IsRefused()
    {
        using var client = Client();
        var email = Email();
        var auth = await EnrollAsync(client, email);
        var userId = auth.GetProperty("user").GetProperty("id").GetGuid();
        var (shopId, _) = await CreateShopAsync(client, auth.GetProperty("accessToken").GetString()!);

        long securityVersion;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            securityVersion = (await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId)).SecurityVersion;
        }

        var legacy = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "inventory.ai.integration",
            audience: "inventory.ai.integration",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim("security_version", securityVersion.ToString(CultureInfo.InvariantCulture)),
                new Claim("active_shop_id", shopId.ToString()),
                new Claim("active_shop_role", "Owner"),
                new Claim("language", "en-IN"),
            ],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("integration-secret-key-must-be-at-least-32-chars!")),
                SecurityAlgorithms.HmacSha256)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/shops/me", legacy)).StatusCode);
    }

    // ── Recovery ends every path, including the hidden issuers ───────────────────

    /// <summary>
    /// Regression guard for the market-readiness finding: after password recovery an access token
    /// minted beforehand must not reach any route that issues credentials or mutates state.
    /// </summary>
    [Theory]
    [InlineData("PUT", "/api/users/me")]
    [InlineData("POST", "/api/shops")]
    [InlineData("POST", "/api/shops/switch")]
    [InlineData("POST", "/api/shops/default")]
    [InlineData("GET", "/api/shops/me")]
    public async Task ResetPassword_OldAccessCannotMintOrMutate(string method, string path)
    {
        using var client = Client();
        var email = Email();
        var auth = await EnrollAsync(client, email);
        var userId = auth.GetProperty("user").GetProperty("id").GetGuid();
        var staleRefresh = auth.GetProperty("refreshToken").GetString()!;
        var (shopId, staleAccess) = await CreateShopAsync(client, auth.GetProperty("accessToken").GetString()!);

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email })).StatusCode);
        var link = _factory.Services.GetRequiredService<ResetLinkCapture>().ResetLink;
        Assert.NotNull(link);
        var token = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(link).Query)["token"].ToString();
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/password-reset/confirm", new { email, token, newPassword = "RecoveredPassword!1" })).StatusCode);

        var before = await ActiveRefreshCountAsync(userId);

        object? body = path switch
        {
            "/api/users/me" => ProfileSave(email, "Hijacked"),
            "/api/shops" => new { name = "Hijacked", address = "1", city = "C", state = "S", pincode = "560001" },
            "/api/shops/switch" or "/api/shops/default" => new { shopId },
            _ => null,
        };

        var response = await SendAsync(client, new HttpMethod(method), path, staleAccess, body);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/token/refresh", new { refreshToken = staleRefresh })).StatusCode);
        Assert.Equal(before, await ActiveRefreshCountAsync(userId));
    }

    internal sealed class ResetLinkCapture : IEmailService
    {
        public string? ResetLink { get; private set; }

        public Task SendPasswordResetAsync(string toEmail, string resetLink, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
        {
            ResetLink = resetLink;
            return Task.CompletedTask;
        }

        public Task SendIdentityAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
