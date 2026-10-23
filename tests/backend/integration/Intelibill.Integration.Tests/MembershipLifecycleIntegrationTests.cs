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
}
