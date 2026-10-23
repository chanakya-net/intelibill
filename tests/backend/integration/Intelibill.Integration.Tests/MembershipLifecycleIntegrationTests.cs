using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

/// <summary>
/// Membership administration is shop-scoped authority: a shop owner administers memberships in
/// their own shop and never the global account behind them. These tests pin the adversarial
/// transitions from the market-readiness review (finding 3 and missing capabilities 2 and 5).
/// </summary>
[Collection("Integration Tests")]
public sealed class MembershipLifecycleIntegrationTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@test.com";

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

    /// <summary>
    /// Fixture-only membership seeding. Public staff onboarding runs through invitations, which
    /// these tests exercise separately; the adversarial setup needs the end state directly.
    /// </summary>
    private async Task SeedMembershipAsync(Guid shopId, Guid userId, ShopRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Add(ShopMembership.Create(shopId, userId, role, isDefault: false));
        await db.SaveChangesAsync();
    }

    private async Task<User> ReadUserAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
    }

    [Fact]
    public async Task OwnerA_CannotChangeStaffOwnerBIdentity()
    {
        using var client = CreateClient();
        var (_, _, ownerAToken) = await EnrollAsync(client, "owner-a");
        var (shopA, ownerAShopToken) = await CreateShopAsync(client, ownerAToken);

        // The shared person: staff in shop A, owner of their own shop B.
        var (sharedUserId, sharedEmail, sharedToken) = await EnrollAsync(client, "shared");
        await CreateShopAsync(client, sharedToken);
        await SeedMembershipAsync(shopA, sharedUserId, ShopRole.Staff);

        var before = await ReadUserAsync(sharedUserId);

        using var attack = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{sharedUserId}");
        attack.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerAShopToken);
        attack.Content = JsonContent.Create(new
        {
            email = sharedEmail,
            firstName = "Seized",
            lastName = "Identity",
            phoneNumber = UniquePhone(),
            role = "Staff",
            isLoginEnabled = false,
        });
        var response = await client.SendAsync(attack);

        Assert.True(
            response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                or HttpStatusCode.MethodNotAllowed or HttpStatusCode.Gone,
            $"Global identity edits must be refused, got {(int)response.StatusCode}.");

        var after = await ReadUserAsync(sharedUserId);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(before.PhoneNumber, after.PhoneNumber);
        Assert.Equal(before.FirstName, after.FirstName);
        Assert.Equal(before.LastName, after.LastName);
        Assert.True(after.IsLoginEnabled, "Shop A must not be able to disable the account that owns shop B.");

        // The recovery identity still belongs to its owner: they can still authenticate.
        var login = await client.PostAsJsonAsync("/api/auth/login", new { identifier = sharedEmail, password = SharedPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task ListShopMembers_DoesNotExposeOtherShops()
    {
        using var client = CreateClient();
        var (_, _, ownerAToken) = await EnrollAsync(client, "owner-a");
        var (shopA, ownerAShopToken) = await CreateShopAsync(client, ownerAToken);

        var (sharedUserId, _, sharedToken) = await EnrollAsync(client, "shared");
        var (shopB, _) = await CreateShopAsync(client, sharedToken);
        await SeedMembershipAsync(shopA, sharedUserId, ShopRole.Staff);

        using var list = new HttpRequestMessage(HttpMethod.Get, "/api/users");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerAShopToken);
        var listResponse = await client.SendAsync(list);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(shopB.ToString(), listBody, StringComparison.OrdinalIgnoreCase);

        using var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/shops/{shopA}/members/{sharedUserId}");
        patch.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerAShopToken);
        patch.Content = JsonContent.Create(new { role = "Manager", status = "Active", expectedVersion = 0L });
        var patchResponse = await client.SendAsync(patch);

        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var patchBody = await patchResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(shopB.ToString(), patchBody, StringComparison.OrdinalIgnoreCase);
        var membership = JsonSerializer.Deserialize<JsonElement>(patchBody);
        Assert.Equal(shopA, membership.GetProperty("shopId").GetGuid());
        Assert.Equal("Manager", membership.GetProperty("role").GetString());
        Assert.Equal("Active", membership.GetProperty("status").GetString());
        Assert.Equal(sharedUserId, membership.GetProperty("userId").GetGuid());
        Assert.False(
            membership.TryGetProperty("shopIds", out _),
            "A membership response must not carry the account's other memberships.");
    }

    private static async Task<HttpResponseMessage> InviteAsync(HttpClient client, string ownerToken, Guid shopId, string email, string role = "Manager")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/shops/{shopId}/invitations");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new { email, role });
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> AcceptAsync(HttpClient client, string accessToken, Guid invitationId, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/invitations/{invitationId}/accept");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new { token });
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PatchMembershipAsync(
        HttpClient client, string ownerToken, Guid shopId, Guid targetUserId, string role, string status, long expectedVersion)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/shops/{shopId}/members/{targetUserId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new { role, status, expectedVersion });
        return await client.SendAsync(request);
    }

    private async Task<int> CountMembershipsAsync(Guid shopId, Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ShopMemberships.CountAsync(sm => sm.ShopId == shopId && sm.UserId == userId);
    }

    [Fact]
    public async Task Invitation_AcceptByWrongVerifiedAddress_Denied()
    {
        using var client = CreateClient();
        var (_, _, ownerToken) = await EnrollAsync(client, "owner");
        var (shopId, ownerShopToken) = await CreateShopAsync(client, ownerToken);

        var (invitedUserId, invitedEmail, _) = await EnrollAsync(client, "invited");
        var (bystanderUserId, _, bystanderToken) = await EnrollAsync(client, "bystander");

        var invited = await InviteAsync(client, ownerShopToken, shopId, invitedEmail);
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        var (invitationId, token) = await IdentityTestMailbox.ReadInvitationAsync(_factory, invitedEmail);

        // A different verified account holding the link must not be able to redeem it.
        var response = await AcceptAsync(client, bystanderToken, invitationId, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Users.InvitationWrongAccount", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await CountMembershipsAsync(shopId, bystanderUserId));
        Assert.Equal(0, await CountMembershipsAsync(shopId, invitedUserId));
    }

    [Fact]
    public async Task Invitation_TwoAccepts_CreateOneMembership()
    {
        using var client = CreateClient();
        var (_, _, ownerToken) = await EnrollAsync(client, "owner");
        var (shopId, ownerShopToken) = await CreateShopAsync(client, ownerToken);

        var (invitedUserId, invitedEmail, invitedToken) = await EnrollAsync(client, "invited");
        (await InviteAsync(client, ownerShopToken, shopId, invitedEmail)).EnsureSuccessStatusCode();
        var (invitationId, token) = await IdentityTestMailbox.ReadInvitationAsync(_factory, invitedEmail);

        // Two clients redeem the same link at once: a retry, a double-click, or a race.
        var first = AcceptAsync(client, invitedToken, invitationId, token);
        var second = AcceptAsync(client, invitedToken, invitationId, token);
        var responses = await Task.WhenAll(first, second);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, await CountMembershipsAsync(shopId, invitedUserId));

        var accepted = responses.Single(r => r.StatusCode == HttpStatusCode.OK);
        var membership = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(shopId, membership.GetProperty("shopId").GetGuid());
        Assert.Equal("Manager", membership.GetProperty("role").GetString());
        Assert.Equal("Active", membership.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Invitation_RevokedBeforeAccept_Denied()
    {
        using var client = CreateClient();
        var (_, _, ownerToken) = await EnrollAsync(client, "owner");
        var (shopId, ownerShopToken) = await CreateShopAsync(client, ownerToken);

        var (invitedUserId, invitedEmail, invitedToken) = await EnrollAsync(client, "invited");
        (await InviteAsync(client, ownerShopToken, shopId, invitedEmail)).EnsureSuccessStatusCode();
        var (invitationId, token) = await IdentityTestMailbox.ReadInvitationAsync(_factory, invitedEmail);

        using var revoke = new HttpRequestMessage(HttpMethod.Delete, $"/api/shops/{shopId}/invitations/{invitationId}");
        revoke.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerShopToken);
        var revoked = await client.SendAsync(revoke);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        var response = await AcceptAsync(client, invitedToken, invitationId, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Users.InvitationNotAcceptable", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await CountMembershipsAsync(shopId, invitedUserId));
    }

    [Fact]
    public async Task RoleChangeRacesInvitationAcceptance_NoStaleGrant()
    {
        using var client = CreateClient();
        var (_, _, ownerToken) = await EnrollAsync(client, "owner");
        var (shopId, ownerShopToken) = await CreateShopAsync(client, ownerToken);

        // An existing staff member, suspended, then re-invited as a Manager.
        var (memberUserId, memberEmail, memberToken) = await EnrollAsync(client, "member");
        await SeedMembershipAsync(shopId, memberUserId, ShopRole.Staff);
        var suspended = await PatchMembershipAsync(client, ownerShopToken, shopId, memberUserId, "Staff", "Suspended", 0);
        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);

        (await InviteAsync(client, ownerShopToken, shopId, memberEmail)).EnsureSuccessStatusCode();
        var (invitationId, token) = await IdentityTestMailbox.ReadInvitationAsync(_factory, memberEmail);

        // The owner changes their mind before the invitation is redeemed. The authority version
        // moves, so the grant issued under the previous decision is void.
        var changed = await PatchMembershipAsync(client, ownerShopToken, shopId, memberUserId, "Staff", "Suspended", 1);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        var response = await AcceptAsync(client, memberToken, invitationId, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Users.InvitationNotAcceptable", await response.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var membership = await db.ShopMemberships.AsNoTracking()
            .SingleAsync(sm => sm.ShopId == shopId && sm.UserId == memberUserId);
        Assert.Equal(MembershipStatus.Suspended, membership.Status);
        Assert.Equal(ShopRole.Staff, membership.Role);
    }

    [Fact]
    public async Task UpdateMembership_WithStaleVersion_Conflicts()
    {
        using var client = CreateClient();
        var (_, _, ownerToken) = await EnrollAsync(client, "owner");
        var (shopId, ownerShopToken) = await CreateShopAsync(client, ownerToken);
        var (memberUserId, _, _) = await EnrollAsync(client, "member");
        await SeedMembershipAsync(shopId, memberUserId, ShopRole.Staff);

        (await PatchMembershipAsync(client, ownerShopToken, shopId, memberUserId, "Manager", "Active", 0))
            .EnsureSuccessStatusCode();

        var stale = await PatchMembershipAsync(client, ownerShopToken, shopId, memberUserId, "Staff", "Removed", 0);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("Users.MembershipVersionConflict", await stale.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SuspendA_LeavesBSessionUsable()
    {
        using var client = CreateClient();
        var (_, _, ownerAToken) = await EnrollAsync(client, "owner-a");
        var (shopA, ownerAShopToken) = await CreateShopAsync(client, ownerAToken);

        // One person, two shops: staff in A, owner of their own B. They are trading in B.
        var (sharedUserId, _, sharedToken) = await EnrollAsync(client, "shared");
        var (shopB, sharedShopBToken) = await CreateShopAsync(client, sharedToken);
        await SeedMembershipAsync(shopA, sharedUserId, ShopRole.Staff);

        var switchedToA = await SwitchAsync(client, sharedShopBToken, shopA);
        Assert.Equal(HttpStatusCode.OK, switchedToA.StatusCode);
        var shopASession = (await switchedToA.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString()!;

        var suspended = await PatchMembershipAsync(client, ownerAShopToken, shopA, sharedUserId, "Staff", "Suspended", 0);
        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
        Assert.Equal("Suspended", (await suspended.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        // The shop-A session is over immediately, without waiting for the token to expire.
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, shopASession, "/api/users")).StatusCode);

        // The same account's shop-B session is untouched, and B no longer offers A.
        var shopBShops = await GetAsync(client, sharedShopBToken, "/api/shops/me");
        Assert.Equal(HttpStatusCode.OK, shopBShops.StatusCode);
        var body = await shopBShops.Content.ReadAsStringAsync();
        Assert.Contains(shopB.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(shopA.ToString(), body, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.Forbidden, (await SwitchAsync(client, sharedShopBToken, shopA)).StatusCode);
    }

    [Fact]
    public async Task Manager_CanSwitchOnlyToActiveAssignedShop()
    {
        using var client = CreateClient();
        var (_, _, ownerToken) = await EnrollAsync(client, "owner");
        var (shopOne, ownerShopOneToken) = await CreateShopAsync(client, ownerToken);
        var (shopTwo, _) = await CreateShopAsync(client, ownerToken);

        var (_, _, otherOwnerToken) = await EnrollAsync(client, "other-owner");
        var (unassignedShop, _) = await CreateShopAsync(client, otherOwnerToken);

        var (managerUserId, _, managerToken) = await EnrollAsync(client, "manager");
        await SeedMembershipAsync(shopOne, managerUserId, ShopRole.Manager);
        await SeedMembershipAsync(shopTwo, managerUserId, ShopRole.Manager);

        // A regional manager assigned to both stores may choose between them: assignment, not
        // ownership, controls selection.
        var intoOne = await SwitchAsync(client, managerToken, shopOne);
        Assert.Equal(HttpStatusCode.OK, intoOne.StatusCode);
        var inOne = await intoOne.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(shopOne, inOne.GetProperty("activeShopId").GetGuid());

        var intoTwo = await SwitchAsync(client, inOne.GetProperty("accessToken").GetString()!, shopTwo);
        Assert.Equal(HttpStatusCode.OK, intoTwo.StatusCode);
        var inTwo = await intoTwo.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(shopTwo, inTwo.GetProperty("activeShopId").GetGuid());

        // A shop they are not assigned to stays out of reach.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SwitchAsync(client, inTwo.GetProperty("accessToken").GetString()!, unassignedShop)).StatusCode);

        // Suspension in one store removes only that store from the choices.
        (await PatchMembershipAsync(client, ownerShopOneToken, shopOne, managerUserId, "Manager", "Suspended", 0))
            .EnsureSuccessStatusCode();

        var afterSuspension = inTwo.GetProperty("accessToken").GetString()!;
        Assert.Equal(HttpStatusCode.Forbidden, (await SwitchAsync(client, afterSuspension, shopOne)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SwitchAsync(client, afterSuspension, shopTwo)).StatusCode);
    }

    private static async Task<HttpResponseMessage> SwitchAsync(HttpClient client, string accessToken, Guid shopId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops/switch");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new { shopId });
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string accessToken, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request);
    }
}
