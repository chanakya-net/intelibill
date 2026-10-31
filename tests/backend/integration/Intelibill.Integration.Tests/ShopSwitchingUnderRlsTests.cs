using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

/// <summary>
/// Shop switching, shop discovery, context-free login and invitation acceptance
/// are cross-shop by design: each one reads or writes rows for a shop that is
/// not the request's active shop, and login and token validation run with no
/// shop context at all. Pinned here, against the restricted runtime role,
/// *before* any policy changes — a predicate that assumed one shop per request
/// would break every one of these, and this file is the gate that catches it.
/// </summary>
[Collection("Integration Tests")]
public sealed class ShopSwitchingUnderRlsTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string SharedPassword = "Pass123!Aa";

    private readonly ApiWebApplicationFactory _factory = new(fixture);

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

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static string UniqueEmail(string prefix) => $"rls-{prefix}-{Guid.NewGuid():N}@test.com";

    private static string UniquePhone() => $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}";

    private static async Task<(Guid UserId, string Email, string AccessToken)> EnrollAsync(HttpClient client, string prefix)
    {
        var email = UniqueEmail(prefix);
        var response = await client.EnrollAsync(new
        {
            email,
            password = SharedPassword,
            firstName = "Verified",
            lastName = "Account",
            phoneNumber = UniquePhone(),
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (
            body.GetProperty("user").GetProperty("id").GetGuid(),
            email,
            body.GetProperty("accessToken").GetString()!);
    }

    private static async Task<(Guid ShopId, string AccessToken)> CreateShopAsync(HttpClient client, string token, string? name = null)
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
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("activeShopId").GetGuid(), body.GetProperty("accessToken").GetString()!);
    }

    private static async Task<HttpResponseMessage> SwitchAsync(HttpClient client, string token, Guid shopId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops/switch");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { shopId });
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    /// <summary>
    /// Reads committed rows through the owner identity. Asserting that a shop's
    /// row was or was not written needs a view the policies do not filter.
    /// </summary>
    private async Task<ShopMembership> ReadMembershipAsync(Guid shopId, Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ShopMemberships
            .AsNoTracking()
            .SingleAsync(sm => sm.ShopId == shopId && sm.UserId == userId);
    }

    [Fact]
    public async Task SwitchToSecondShop_ReissuesScopedToken()
    {
        using var client = CreateClient();
        var (userId, _, token) = await EnrollAsync(client, "switcher");
        var (first, firstToken) = await CreateShopAsync(client, token, "First Shop");
        var (_, secondToken) = await CreateShopAsync(client, firstToken, "Second Shop");

        var beforeSwitch = await ReadMembershipAsync(first, userId);

        // Scoped to the second shop, this writes the *first* shop's membership row.
        var response = await SwitchAsync(client, secondToken, first);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(first, body.GetProperty("activeShopId").GetGuid());
        var reissued = body.GetProperty("accessToken").GetString()!;
        Assert.NotEqual(secondToken, reissued);

        // The persisted write, not only the token: MarkUsed on the target shop's row.
        var afterSwitch = await ReadMembershipAsync(first, userId);
        Assert.NotNull(afterSwitch.LastUsedAt);
        Assert.True(
            afterSwitch.LastUsedAt > beforeSwitch.LastUsedAt,
            $"Switching must persist last_used_at on the target shop's membership (was {beforeSwitch.LastUsedAt:O}, now {afterSwitch.LastUsedAt:O}).");

        // The reissued token works, and it is scoped to the shop that was switched into.
        var afterRequest = await GetAsync(client, reissued, $"/api/shops/{first}");
        Assert.Equal(HttpStatusCode.OK, afterRequest.StatusCode);
    }

    [Fact]
    public async Task ListMyShops_ReturnsEveryActiveMembership()
    {
        using var client = CreateClient();
        var (_, _, token) = await EnrollAsync(client, "picker");
        var (first, firstToken) = await CreateShopAsync(client, token, "Picker First");
        var (second, secondToken) = await CreateShopAsync(client, firstToken, "Picker Second");

        var response = await GetAsync(client, secondToken, "/api/shops/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var shops = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = shops.EnumerateArray().Select(shop => shop.GetProperty("shopId").GetGuid()).ToList();
        Assert.Contains(first, ids);
        Assert.Contains(second, ids);
    }

    [Fact]
    public async Task LoginWithTwoShops_SelectsMostRecentlyUsed()
    {
        using var client = CreateClient();
        var (_, email, token) = await EnrollAsync(client, "returning");
        var (first, firstToken) = await CreateShopAsync(client, token, "Returning First");
        var (_, secondToken) = await CreateShopAsync(client, firstToken, "Returning Second");
        (await SwitchAsync(client, secondToken, first)).EnsureSuccessStatusCode();

        // No token, so both app.current_user_id and app.active_shop_id are empty
        // while the memberships are read and the selection is written.
        var response = await client.PostAsJsonAsync("/api/auth/login", new { identifier = email, password = SharedPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(first, body.GetProperty("activeShopId").GetGuid());
        Assert.Equal(2, body.GetProperty("shops").GetArrayLength());
    }

    [Fact]
    public async Task LoginWithoutShop_CanDiscoverOnlyOwnMemberships()
    {
        using var client = CreateClient();
        var (_, _, ownerToken) = await EnrollAsync(client, "stranger-owner");
        var (otherShop, _) = await CreateShopAsync(client, ownerToken, "Not Yours");

        var (_, shoplessEmail, _) = await EnrollAsync(client, "shopless");

        var response = await client.PostAsJsonAsync("/api/auth/login", new { identifier = shoplessEmail, password = SharedPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("activeShopId").ValueKind);
        Assert.Equal(0, body.GetProperty("shops").GetArrayLength());
        Assert.DoesNotContain(otherShop.ToString(), await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AcceptInvitation_WhileScopedToAnotherShop_Succeeds()
    {
        using var client = CreateClient();
        var (_, _, ownerToken) = await EnrollAsync(client, "inviter");
        var (invitedShop, ownerShopToken) = await CreateShopAsync(client, ownerToken, "Invited Shop");

        // The invited account already runs a shop of its own, so its session is
        // scoped to a shop it is not being invited into.
        var (invitedUserId, invitedEmail, invitedToken) = await EnrollAsync(client, "joiner");
        var (ownShop, invitedScopedToken) = await CreateShopAsync(client, invitedToken, "Joiner Own Shop");

        using var invite = new HttpRequestMessage(HttpMethod.Post, $"/api/shops/{invitedShop}/invitations");
        invite.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerShopToken);
        invite.Content = JsonContent.Create(new { email = invitedEmail, role = "Manager" });
        (await client.SendAsync(invite)).EnsureSuccessStatusCode();
        var (invitationId, invitationToken) = await IdentityTestMailbox.ReadInvitationAsync(_factory, invitedEmail);

        // Accepting locks the *inviter's* membership row in a shop the actor is not
        // a member of yet, while the request's context still names the actor's own shop.
        using var accept = new HttpRequestMessage(HttpMethod.Post, $"/api/invitations/{invitationId}/accept");
        accept.Headers.Authorization = new AuthenticationHeaderValue("Bearer", invitedScopedToken);
        accept.Content = JsonContent.Create(new { token = invitationToken });
        var response = await client.SendAsync(accept);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var membership = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(invitedShop, membership.GetProperty("shopId").GetGuid());
        Assert.Equal("Manager", membership.GetProperty("role").GetString());

        var persisted = await ReadMembershipAsync(invitedShop, invitedUserId);
        Assert.Equal(Domain.Enums.ShopRole.Manager, persisted.Role);
        Assert.NotEqual(ownShop, persisted.ShopId);
    }

    [Fact]
    public async Task SetDefaultShop_ClearsPreviousDefaultAcrossShops()
    {
        using var client = CreateClient();
        var (userId, _, token) = await EnrollAsync(client, "defaults");
        var (first, firstToken) = await CreateShopAsync(client, token, "Default First");
        var (second, secondToken) = await CreateShopAsync(client, firstToken, "Default Second");

        Assert.True((await ReadMembershipAsync(first, userId)).IsDefault);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops/default");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secondToken);
        request.Content = JsonContent.Create(new { shopId = second });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await ReadMembershipAsync(second, userId)).IsDefault);
        Assert.False(
            (await ReadMembershipAsync(first, userId)).IsDefault,
            "Setting a default must clear the previous default, which lives in another shop.");
    }

    [Fact]
    public async Task MemberList_ShowsCoMembersOfTheRequestedShop()
    {
        using var client = CreateClient();
        var (ownerId, _, ownerToken) = await EnrollAsync(client, "roster-owner");
        var (shopId, ownerShopToken) = await CreateShopAsync(client, ownerToken, "Roster Shop");

        var (memberId, memberEmail, memberToken) = await EnrollAsync(client, "roster-member");
        using var invite = new HttpRequestMessage(HttpMethod.Post, $"/api/shops/{shopId}/invitations");
        invite.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerShopToken);
        invite.Content = JsonContent.Create(new { email = memberEmail, role = "Staff" });
        (await client.SendAsync(invite)).EnsureSuccessStatusCode();
        var (invitationId, invitationToken) = await IdentityTestMailbox.ReadInvitationAsync(_factory, memberEmail);

        using var accept = new HttpRequestMessage(HttpMethod.Post, $"/api/invitations/{invitationId}/accept");
        accept.Headers.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        accept.Content = JsonContent.Create(new { token = invitationToken });
        (await client.SendAsync(accept)).EnsureSuccessStatusCode();

        var response = await GetAsync(client, ownerShopToken, "/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(ownerId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(memberId.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProtectedRequest_SucceedsWhenTokenValidationRunsWithoutContext()
    {
        using var client = CreateClient();

        // LiveSessionValidator runs inside OnTokenValidated, before HttpContext.User is
        // assigned, so its reads of auth_sessions, users and shop_memberships execute
        // with an empty session context. An account with no shop keeps that context
        // empty for the whole request.
        var (_, _, shoplessToken) = await EnrollAsync(client, "no-shop");

        var shopless = await GetAsync(client, shoplessToken, "/api/shops/me");
        Assert.Equal(HttpStatusCode.OK, shopless.StatusCode);
        Assert.Equal(0, (await shopless.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());

        var (_, _, token) = await EnrollAsync(client, "with-shop");
        var (shopId, shopToken) = await CreateShopAsync(client, token, "Validated Shop");

        var scoped = await GetAsync(client, shopToken, $"/api/shops/{shopId}");
        Assert.Equal(HttpStatusCode.OK, scoped.StatusCode);
    }
}
