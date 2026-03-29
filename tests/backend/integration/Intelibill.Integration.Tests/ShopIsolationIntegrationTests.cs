using System.IdentityModel.Tokens.Jwt;
using ErrorOr;
using FluentValidation;
using FluentValidation.Results;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Shops.Commands.CreateShop;
using Intelibill.Application.Features.Shops.Commands.SetDefaultShop;
using Intelibill.Application.Features.Shops.Commands.SwitchActiveShop;
using Intelibill.Application.Features.Shops.Commands.UpdateShop;
using Intelibill.Application.Features.Shops.Queries.GetMyShops;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public class ShopIsolationIntegrationTests
{
    private static Shop CreateTestShop(string name, string pincode = "560001") =>
        Shop.Create(name, "Address", "City", "State", pincode, null, null, null);

    [Fact]
    public async Task SwitchActiveShop_WhenUserDoesNotOwnShop_ReturnsMembershipForbiddenError()
    {
        var user = User.CreateWithEmail("user@test.com", "hash", "First", "Last");
        var ownShop = CreateTestShop("Own");
        var ownMembership = ShopMembership.Create(ownShop.Id, user.Id, ShopRole.Owner, true);
        ownShop.AddMembership(ownMembership);
        user.AddShopMembership(ownMembership);

        var tokenService = BuildTokenService();
        var userRepository = new InMemoryUserRepository(user);
        var refreshTokenRepository = new InMemoryRefreshTokenRepository();
        var unitOfWork = new InMemoryUnitOfWork();

        var handler = new SwitchActiveShopCommandHandler(userRepository, refreshTokenRepository, tokenService, unitOfWork);
        var result = await handler.HandleAsync(new SwitchActiveShopCommand(user.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Shop.MembershipNotFound.Code);
    }

    [Fact]
    public async Task SwitchActiveShop_ForOwnedShop_RotatesTokenWithActiveShopClaim()
    {
        var user = User.CreateWithEmail("user@test.com", "hash", "First", "Last");
        var firstShop = CreateTestShop("First");
        var firstMembership = ShopMembership.Create(firstShop.Id, user.Id, ShopRole.Owner, true);
        firstShop.AddMembership(firstMembership);
        user.AddShopMembership(firstMembership);

        var secondShop = CreateTestShop("Second", "560002");
        var secondMembership = ShopMembership.Create(secondShop.Id, user.Id, ShopRole.Owner, false);
        secondShop.AddMembership(secondMembership);
        user.AddShopMembership(secondMembership);

        var tokenService = BuildTokenService();
        var userRepository = new InMemoryUserRepository(user);
        var refreshTokenRepository = new InMemoryRefreshTokenRepository();
        var unitOfWork = new InMemoryUnitOfWork();

        var handler = new SwitchActiveShopCommandHandler(userRepository, refreshTokenRepository, tokenService, unitOfWork);
        var result = await handler.HandleAsync(new SwitchActiveShopCommand(user.Id, secondShop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(secondShop.Id, result.Value.ActiveShopId);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Value.AccessToken);
        var activeShopClaim = jwt.Claims.FirstOrDefault(c => c.Type == "active_shop_id")?.Value;
        var activeRoleClaim = jwt.Claims.FirstOrDefault(c => c.Type == "active_shop_role")?.Value;
        Assert.Equal(secondShop.Id.ToString(), activeShopClaim);
        Assert.Equal("Owner", activeRoleClaim);

        Assert.Single(refreshTokenRepository.AddedTokens);
        Assert.True(unitOfWork.SaveChangesCalled);
    }

    [Fact]
    public async Task SwitchActiveShop_WhenTargetRoleIsNotOwner_ReturnsOwnerOnlyError()
    {
        var user = User.CreateWithEmail("user@test.com", "hash", "First", "Last");
        var firstShop = CreateTestShop("First");
        var firstMembership = ShopMembership.Create(firstShop.Id, user.Id, ShopRole.Owner, true);
        firstShop.AddMembership(firstMembership);
        user.AddShopMembership(firstMembership);

        var secondShop = CreateTestShop("Second", "560002");
        var secondMembership = ShopMembership.Create(secondShop.Id, user.Id, ShopRole.Manager, false);
        secondShop.AddMembership(secondMembership);
        user.AddShopMembership(secondMembership);

        var tokenService = BuildTokenService();
        var userRepository = new InMemoryUserRepository(user);
        var refreshTokenRepository = new InMemoryRefreshTokenRepository();
        var unitOfWork = new InMemoryUnitOfWork();

        var handler = new SwitchActiveShopCommandHandler(userRepository, refreshTokenRepository, tokenService, unitOfWork);
        var result = await handler.HandleAsync(new SwitchActiveShopCommand(user.Id, secondShop.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Shop.UserIsNotOwnerForSwitch.Code);
    }

    [Fact]
    public async Task CreateShop_WhenNameBlank_ReturnsNameRequiredError()
    {
        var user = User.CreateWithEmail("user@test.com", "hash", "First", "Last");
        var tokenService = BuildTokenService();
        var userRepository = new InMemoryUserRepository(user);
        var shopRepository = new InMemoryShopRepository();
        var refreshTokenRepository = new InMemoryRefreshTokenRepository();
        var unitOfWork = new InMemoryUnitOfWork();

        var handler = new CreateShopCommandHandler(
            userRepository,
            shopRepository,
            refreshTokenRepository,
            tokenService,
            unitOfWork);

        var result = await handler.HandleAsync(
            new CreateShopCommand(user.Id, "   ", "Address", "City", "State", "560001", null, null, null),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Shop.NameRequired.Code);
        Assert.Empty(shopRepository.AddedShops);
    }

    [Fact]
    public async Task CreateShop_WhenUserMissing_ReturnsUserNotFoundError()
    {
        var tokenService = BuildTokenService();
        var userRepository = new InMemoryUserRepository(null);
        var shopRepository = new InMemoryShopRepository();
        var refreshTokenRepository = new InMemoryRefreshTokenRepository();
        var unitOfWork = new InMemoryUnitOfWork();

        var handler = new CreateShopCommandHandler(
            userRepository,
            shopRepository,
            refreshTokenRepository,
            tokenService,
            unitOfWork);

        var result = await handler.HandleAsync(
            new CreateShopCommand(Guid.NewGuid(), "Main", "Address", "City", "State", "560001", null, null, null),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Shop.UserNotFound.Code);
        Assert.Empty(shopRepository.AddedShops);
    }

    [Fact]
    public async Task CreateShop_WhenValid_CreatesDefaultShopAndReturnsAuthResult()
    {
        var user = User.CreateWithEmail("user@test.com", "hash", "First", "Last");
        var tokenService = BuildTokenService();
        var userRepository = new InMemoryUserRepository(user);
        var shopRepository = new InMemoryShopRepository();
        var refreshTokenRepository = new InMemoryRefreshTokenRepository();
        var unitOfWork = new InMemoryUnitOfWork();

        var handler = new CreateShopCommandHandler(
            userRepository,
            shopRepository,
            refreshTokenRepository,
            tokenService,
            unitOfWork);

        var result = await handler.HandleAsync(
            new CreateShopCommand(
                user.Id,
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
        Assert.Equal("Main Shop", shopRepository.AddedShops.Single().Name);
        Assert.Equal("42 MG Road", shopRepository.AddedShops.Single().Address);
        Assert.Equal("Bengaluru", shopRepository.AddedShops.Single().City);
        Assert.Equal("Karnataka", shopRepository.AddedShops.Single().State);
        Assert.Equal("560001", shopRepository.AddedShops.Single().Pincode);
        Assert.Equal("Chandra", shopRepository.AddedShops.Single().ContactPerson);
        Assert.Equal("9876543210", shopRepository.AddedShops.Single().MobileNumber);
        Assert.Equal("27AAPFU0939F1ZV", shopRepository.AddedShops.Single().GstNumber);
        Assert.Equal(user.Id, result.Value.User.Id);
        Assert.NotNull(result.Value.ActiveShopId);
        Assert.NotNull(result.Value.Shops);
        var onlyShop = Assert.Single(result.Value.Shops!);
        Assert.True(onlyShop.IsDefault);
        Assert.Single(refreshTokenRepository.AddedTokens);
        Assert.True(unitOfWork.SaveChangesCalled);
    }

    [Fact]
    public async Task SetDefaultShop_WhenUserMissing_ReturnsUserNotFoundError()
    {
        var tokenService = BuildTokenService();
        var userRepository = new InMemoryUserRepository(null);
        var refreshTokenRepository = new InMemoryRefreshTokenRepository();
        var unitOfWork = new InMemoryUnitOfWork();

        var handler = new SetDefaultShopCommandHandler(userRepository, refreshTokenRepository, tokenService, unitOfWork);
        var result = await handler.HandleAsync(new SetDefaultShopCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Shop.UserNotFound.Code);
    }

    [Fact]
    public async Task SetDefaultShop_WhenMembershipMissing_ReturnsForbiddenError()
    {
        var user = User.CreateWithEmail("user@test.com", "hash", "First", "Last");
        var existingShop = CreateTestShop("Existing");
        var existingMembership = ShopMembership.Create(existingShop.Id, user.Id, ShopRole.Owner, true);
        existingMembership.MarkUsed();
        existingShop.AddMembership(existingMembership);
        user.AddShopMembership(existingMembership);

        var tokenService = BuildTokenService();
        var userRepository = new InMemoryUserRepository(user);
        var refreshTokenRepository = new InMemoryRefreshTokenRepository();
        var unitOfWork = new InMemoryUnitOfWork();

        var handler = new SetDefaultShopCommandHandler(userRepository, refreshTokenRepository, tokenService, unitOfWork);
        var result = await handler.HandleAsync(new SetDefaultShopCommand(user.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Shop.MembershipNotFound.Code);
    }

    [Fact]
    public async Task SetDefaultShop_WhenValid_UpdatesDefaultAndReturnsAuthResult()
    {
        var user = User.CreateWithEmail("user@test.com", "hash", "First", "Last");

        var firstShop = CreateTestShop("First");
        var firstMembership = ShopMembership.Create(firstShop.Id, user.Id, ShopRole.Owner, true);
        firstMembership.MarkUsed();
        firstShop.AddMembership(firstMembership);
        user.AddShopMembership(firstMembership);

        var secondShop = CreateTestShop("Second", "560002");
        var secondMembership = ShopMembership.Create(secondShop.Id, user.Id, ShopRole.Manager, false);
        secondShop.AddMembership(secondMembership);
        user.AddShopMembership(secondMembership);

        var tokenService = BuildTokenService();
        var userRepository = new InMemoryUserRepository(user);
        var refreshTokenRepository = new InMemoryRefreshTokenRepository();
        var unitOfWork = new InMemoryUnitOfWork();

        var handler = new SetDefaultShopCommandHandler(userRepository, refreshTokenRepository, tokenService, unitOfWork);
        var result = await handler.HandleAsync(new SetDefaultShopCommand(user.Id, secondShop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(secondShop.Id, result.Value.ActiveShopId);
        Assert.False(firstMembership.IsDefault);
        Assert.True(secondMembership.IsDefault);
        Assert.Single(refreshTokenRepository.AddedTokens);
        Assert.True(unitOfWork.SaveChangesCalled);
    }

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
        var shopRepository = new InMemoryShopRepository();
        var unitOfWork = new InMemoryUnitOfWork();

        var handler = new UpdateShopCommandHandler(
            BuildValidUpdateShopValidator(),
            userRepository,
            shopRepository,
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
        var unitOfWork = new InMemoryUnitOfWork();

        var handler = new UpdateShopCommandHandler(
            BuildValidUpdateShopValidator(),
            userRepository,
            shopRepository,
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

    private static ITokenService BuildTokenService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Host"] = "localhost",
                ["Database:Port"] = "5432",
                ["Database:Database"] = "integration",
                ["Database:Username"] = "integration",
                ["Database:Password"] = "integration",
                ["Jwt:Secret"] = "integration-secret-key-must-be-at-least-32-chars!",
                ["Jwt:Issuer"] = "inventory.ai.integration",
                ["Jwt:Audience"] = "inventory.ai.integration",
                ["Jwt:AccessTokenExpiryMinutes"] = "15",
                ["Jwt:RefreshTokenExpiryDays"] = "7"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider().GetRequiredService<ITokenService>();
    }

    private static InlineValidator<UpdateShopCommand> BuildValidUpdateShopValidator()
    {
        var validator = new InlineValidator<UpdateShopCommand>();
        return validator;
    }

    private sealed class InMemoryUserRepository(User? user) : IUserRepository
    {
        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
        public Task<User?> GetByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
        public Task<User?> GetByExternalLoginAsync(ExternalAuthProvider provider, string providerKey, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
        public Task<User?> GetByIdWithDetailsAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(user is not null && user.Id == userId ? user : null);
        public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<User>>([]);
        public Task<IReadOnlyList<User>> FindAsync(System.Linq.Expressions.Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<User>>([]);
        public Task AddAsync(User entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Update(User entity) { }
        public void Remove(User entity) { }
    }

    private sealed class InMemoryShopRepository : IShopRepository
    {
        public List<Shop> AddedShops { get; } = [];

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

        public Task<Shop?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<Shop?>(null);

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

    private sealed class InMemoryRefreshTokenRepository : IRefreshTokenRepository
    {
        public List<RefreshToken> AddedTokens { get; } = [];

        public Task<RefreshToken?> GetActiveByTokenAsync(string token, CancellationToken cancellationToken = default) => Task.FromResult<RefreshToken?>(null);
        public Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<RefreshToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<RefreshToken?>(null);
        public Task<IReadOnlyList<RefreshToken>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RefreshToken>>([]);
        public Task<IReadOnlyList<RefreshToken>> FindAsync(System.Linq.Expressions.Expression<Func<RefreshToken, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RefreshToken>>([]);

        public Task AddAsync(RefreshToken entity, CancellationToken cancellationToken = default)
        {
            AddedTokens.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(RefreshToken entity) { }
        public void Remove(RefreshToken entity) { }
    }

    private sealed class InMemoryUnitOfWork : IUnitOfWork
    {
        public bool SaveChangesCalled { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalled = true;
            return Task.FromResult(1);
        }
    }
}