using System.IdentityModel.Tokens.Jwt;
using ErrorOr;
using FluentValidation;
using FluentValidation.Results;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Shops.Commands.UpdateShop;
using Intelibill.Application.Features.Shops.Queries.GetMyShops;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure;
using Intelibill.Infrastructure.Options;
using Intelibill.Infrastructure.Services.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        var user = User.CreateWithEmail("owner@test.com", "hash", "First", "Last");
        var shop = CreateTestShop("Old Shop");
        var membership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, true);
        shop.AddMembership(membership);
        user.AddShopMembership(membership);

        var userRepository = new InMemoryUserRepository(user);
        var shopRepository = new InMemoryShopRepository(shop);
        var bankAccountRepository = new InMemoryBankAccountRepository();
        var unitOfWork = new InMemoryUnitOfWork();

        var handler = new UpdateShopCommandHandler(
            userRepository,
            shopRepository,
            bankAccountRepository,
            unitOfWork);

        var result = await handler.HandleAsync(
            new UpdateShopCommand(
                user.Id,
                shop.Id,
                "  Main Shop  ",
                "  42 MG Road  ",
                "  Bengaluru  ",
                "  Karnataka  ",
                "  560001  ",
                "  Chandra  ",
                "  9876543210  ",
                "  27AAPFU0939F1ZV  "),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Main Shop", shop.Name);
        Assert.Equal("42 MG Road", shop.Address);
        Assert.Equal("Bengaluru", shop.City);
        Assert.Equal("Karnataka", shop.State);
        Assert.Equal("560001", shop.Pincode);
        Assert.Equal("Chandra", shop.ContactPerson);
        Assert.Equal("9876543210", shop.MobileNumber);
        Assert.Equal("27AAPFU0939F1ZV", shop.GstNumber);
        Assert.True(unitOfWork.SaveChangesCalled);
    }

    [Fact]
    public async Task UpdateShop_WhenUserIsNotOwner_ReturnsForbiddenError()
    {
        var user = User.CreateWithEmail("manager@test.com", "hash", "First", "Last");
        var shop = CreateTestShop("Main");
        var membership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Manager, true);
        shop.AddMembership(membership);
        user.AddShopMembership(membership);

        var userRepository = new InMemoryUserRepository(user);
        var shopRepository = new InMemoryShopRepository();
        var bankAccountRepository = new InMemoryBankAccountRepository();
        var unitOfWork = new InMemoryUnitOfWork();

        var handler = new UpdateShopCommandHandler(
            userRepository,
            shopRepository,
            bankAccountRepository,
            unitOfWork);

        var result = await handler.HandleAsync(
            new UpdateShopCommand(
                user.Id,
                shop.Id,
                "Main Shop",
                "Address",
                "City",
                "State",
                "560001",
                null,
                null,
                null),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Shop.UserIsNotOwner.Code);
    }

    private sealed class InMemoryUserRepository(User? user) : IUserRepository
    {
        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
        public Task<User?> GetByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
        public Task<User?> GetByIdWithDetailsAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(user is not null && user.Id == userId ? user : null);
        public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsByPhoneAsync(string phoneNumber, Guid excludedUserId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<User>>([]);
        public Task<IReadOnlyList<User>> FindAsync(System.Linq.Expressions.Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<User>>([]);
        public Task AddAsync(User entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Update(User entity) { }
        public void Remove(User entity) { }
    }

    private sealed class InMemoryShopRepository : IShopRepository
    {
        private readonly Shop? _existingShop;

        public InMemoryShopRepository(Shop? existingShop = null)
        {
            _existingShop = existingShop;
        }

        public List<Shop> AddedShops { get; } = [];

        public Task<Shop?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_existingShop?.Id == id ? _existingShop : null);

        public Task<IReadOnlyList<ShopMembership>> GetMembershipsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ShopMembership>>([]);

        public Task<ShopMembership?> GetMembershipAsync(Guid userId, Guid shopId, CancellationToken cancellationToken = default)
            => Task.FromResult<ShopMembership?>(null);

        public Task<ShopMembership?> GetDefaultMembershipAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<ShopMembership?>(null);

        public Task<ShopMembership?> GetMostRecentlyUsedMembershipAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<ShopMembership?>(null);

        public Task<bool> UserHasMembershipAsync(Guid userId, Guid shopId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task ClearDefaultForUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<Shop?> GetByIdWithMembersAsync(Guid shopId, CancellationToken cancellationToken = default)
            => Task.FromResult<Shop?>(null);

        public Task<IReadOnlyList<ShopMembership>> GetMembershipsForUsersInShopsAsync(IReadOnlyList<Guid> userIds, IReadOnlyList<Guid> shopIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ShopMembership>>([]);

        public Task AddMembershipAsync(ShopMembership membership, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void RemoveMembership(ShopMembership membership) { }

        public Task<IReadOnlyList<Shop>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Shop>>([]);

        public Task<IReadOnlyList<Shop>> FindAsync(System.Linq.Expressions.Expression<Func<Shop, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Shop>>([]);

        public Task AddAsync(Shop entity, CancellationToken cancellationToken = default)
        {
            AddedShops.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(Shop entity) { }
        public void Remove(Shop entity) { }
    }

    private sealed class InMemorySupplierRepository : ISupplierRepository
    {
        public List<Supplier> AddedSuppliers { get; } = [];
        public Task<IReadOnlyList<Supplier>> GetByShopIdAsync(Guid shopId, bool includeSystem, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Supplier>>([]);
        public Task<Supplier?> GetSystemByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default) => Task.FromResult<Supplier?>(null);
        public Task<Supplier?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Supplier?>(null);
        public Task<IReadOnlyList<Supplier>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Supplier>>([]);
        public Task<IReadOnlyList<Supplier>> FindAsync(System.Linq.Expressions.Expression<Func<Supplier, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Supplier>>([]);
        public Task AddAsync(Supplier entity, CancellationToken cancellationToken = default) { AddedSuppliers.Add(entity); return Task.CompletedTask; }
        public void Update(Supplier entity) { }
        public void Remove(Supplier entity) { }
    }

    private sealed class InMemoryBankAccountRepository : IBankAccountRepository
    {
        public Task<IReadOnlyList<BankAccount>> GetByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BankAccount>>([]);

        public Task<BankAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<BankAccount?>(null);

        public Task<IReadOnlyList<BankAccount>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BankAccount>>([]);

        public Task<IReadOnlyList<BankAccount>> FindAsync(System.Linq.Expressions.Expression<Func<BankAccount, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BankAccount>>([]);

        public Task AddAsync(BankAccount entity, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Update(BankAccount entity) { }
        public void Remove(BankAccount entity) { }
    }

    private sealed class InMemoryUnitOfWork : IUnitOfWork
    {
        public bool SaveChangesCalled { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalled = true;
            return Task.FromResult(1);
        }

        public void ClearChanges() { }

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
