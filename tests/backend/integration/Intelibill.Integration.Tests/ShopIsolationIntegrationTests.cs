using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Shops.Queries.GetMyShops;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class ShopIsolationIntegrationTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    private static Shop CreateTestShop(string name, string pincode = "560001") =>
        Shop.Create(name, "Address", "City", "State", pincode, null, null, null);

    // The shop-switch, create-shop and default-shop handler cases that used to live here were
    // removed rather than rewritten: the rules they asserted now belong to the session service,
    // where they are tested directly against it, and the end-to-end behaviour — including that a
    // switch moves only the requesting device — is covered against real PostgreSQL in
    // SessionLifecycleIntegrationTests.

    [Fact]
    public async Task GetMyShops_WhenUserMissing_ReturnsUserNotFoundError()
    {
        var handler = new GetMyShopsQueryHandler(new InMemoryUserRepository(null));

        var result = await handler.HandleAsync(new GetMyShopsQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Shop.UserNotFound.Code);
    }

    [Fact]
    public async Task GetMyShops_WhenValid_ReturnsDefaultShopFirst()
    {
        var user = User.CreateWithEmail("user@test.com", "hash", "First", "Last");
        user.VerifyEmail("user@test.com");

        var firstShop = CreateTestShop("First");
        var firstMembership = ShopMembership.Create(firstShop.Id, user.Id, ShopRole.Owner, false);
        firstMembership.MarkUsed();
        firstShop.AddMembership(firstMembership);
        user.AddShopMembership(firstMembership);

        var secondShop = CreateTestShop("Second", "560002");
        var secondMembership = ShopMembership.Create(secondShop.Id, user.Id, ShopRole.Manager, true);
        secondShop.AddMembership(secondMembership);
        user.AddShopMembership(secondMembership);

        var handler = new GetMyShopsQueryHandler(new InMemoryUserRepository(user));
        var result = await handler.HandleAsync(new GetMyShopsQuery(user.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(secondShop.Id, result.Value[0].ShopId);
        Assert.True(result.Value[0].IsDefault);
    }

    [Fact]
    public async Task UpdateShop_WhenOwner_UpdatesDetailsAndPersists()
    {
        using var client = CreateClient();
        var shopId = await EnrollAndCreateShopAsync(client, $"owner-{Guid.NewGuid():N}@fixture.test");
        using var response = await client.PutAsJsonAsync($"/api/shops/{shopId}", UpdateProfile);
        response.EnsureSuccessStatusCode();
        await using var db = Database();
        var shop = await db.Shops.SingleAsync(x => x.Id == shopId);
        Assert.Equal(("Main Shop", "42 MG Road", "Bengaluru", "Karnataka", "560001"),
            (shop.Name, shop.Address, shop.City, shop.State, shop.Pincode));
        Assert.Equal(("Chandra", "9876543210", "27AAPFU0939F1ZV"), (shop.ContactPerson, shop.MobileNumber, shop.GstNumber));
    }

    [Fact]
    public async Task UpdateShop_WhenUserIsNotOwner_ReturnsForbiddenError()
    {
        using var client = CreateClient();
        var email = $"manager-{Guid.NewGuid():N}@fixture.test";
        var shopId = await EnrollAndCreateShopAsync(client, email);
        await using var db = Database();
        var membership = await db.ShopMemberships.SingleAsync(x => x.ShopId == shopId);
        membership.ChangeAuthority(ShopRole.Manager, MembershipStatus.Active);
        await db.SaveChangesAsync();
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { identifier = email, password = "Pass123!Aa" });
        Authenticate(client, await ReadAsync(login));

        using var response = await client.PutAsJsonAsync($"/api/shops/{shopId}", UpdateProfile);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Shop.UserIsNotOwner", problem.GetProperty("title").GetString());
        Assert.Equal("Old Shop", await db.Shops.Where(x => x.Id == shopId).Select(x => x.Name).SingleAsync());
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost") });

    private ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(fixture.MigratorConnectionString).UseSnakeCaseNamingConvention().Options);

    private static async Task<Guid> EnrollAndCreateShopAsync(HttpClient client, string email)
    {
        using var enrolled = await client.EnrollAsync(new
        {
            email, password = "Pass123!Aa", firstName = "Fixture", lastName = "Owner",
        });
        Authenticate(client, await ReadAsync(enrolled));
        using var created = await client.PostAsJsonAsync("/api/shops", new
            { name = "Old Shop", address = "Old Road", city = "City", state = "State", pincode = "560002" });
        var result = await ReadAsync(created);
        Authenticate(client, result);
        return result.GetProperty("activeShopId").GetGuid();
    }

    private static object UpdateProfile => new
    {
        name = "  Main Shop  ", address = "  42 MG Road  ", city = "  Bengaluru  ", state = "  Karnataka  ",
        pincode = "  560001  ", contactPerson = "  Chandra  ", mobileNumber = "  9876543210  ", gstNumber = "  27AAPFU0939F1ZV  ",
    };

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static void Authenticate(HttpClient client, JsonElement response) =>
        client.DefaultRequestHeaders.Authorization = new("Bearer", response.GetProperty("accessToken").GetString());

    private sealed class InMemoryUserRepository(User? user) : IUserRepository
    {
        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
        public Task<User?> GetByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
        public Task<User?> GetByIdWithDetailsAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(user is not null && user.Id == userId ? user : null);
        public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsByPhoneAsync(string phoneNumber, Guid excludedUserId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(Guid shopId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<User>>([]);
        public Task<IReadOnlyList<User>> FindAsync(System.Linq.Expressions.Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<User>>([]);
        public Task AddAsync(User entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Update(User entity) { }
        public void Remove(User entity) { }
    }

}
