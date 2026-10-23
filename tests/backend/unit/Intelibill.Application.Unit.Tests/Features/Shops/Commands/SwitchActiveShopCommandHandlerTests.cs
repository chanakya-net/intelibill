using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Shops.Commands.SwitchActiveShop;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Shops.Commands.SwitchActiveShop;

/// <summary>
/// Selection is controlled by live assignment, not ownership: a regional manager assigned to
/// several stores can choose between them, and role still governs what they may do inside one.
/// The previous test asserted the owner-only rule and is replaced.
/// </summary>
public class SwitchActiveShopCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private SwitchActiveShopCommandHandler CreateHandler()
    {
        _tokenService
            .GenerateAccessTokenAsync(Arg.Any<User>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(("access-token", DateTimeOffset.UtcNow.AddMinutes(15)));
        _tokenService
            .CreateRefreshToken(Arg.Any<Guid>())
            .Returns(call => RefreshToken.Create(call.Arg<Guid>(), "refresh-token", DateTimeOffset.UtcNow.AddDays(7)));

        return new SwitchActiveShopCommandHandler(_userRepository, _refreshTokenRepository, _tokenService, _unitOfWork);
    }

    [Fact]
    public async Task HandleAsync_WhenCallerIsAnActiveManager_SwitchesToTheAssignedShop()
    {
        var user = User.CreateWithEmail("manager@test.com", "hash", "Manager", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var membership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Manager, true);
        shop.AddMembership(membership);
        user.AddShopMembership(membership);

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateHandler().HandleAsync(
            new SwitchActiveShopCommand(user.Id, shop.Id, user.SecurityVersion),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(shop.Id, result.Value.ActiveShopId);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCallerHasNoMembershipInTheShop_ReturnsForbidden()
    {
        var user = User.CreateWithEmail("manager@test.com", "hash", "Manager", "One");
        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateHandler().HandleAsync(
            new SwitchActiveShopCommand(user.Id, Guid.NewGuid(), user.SecurityVersion),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenTheMembershipIsSuspended_ReturnsForbidden()
    {
        var user = User.CreateWithEmail("manager@test.com", "hash", "Manager", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var membership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Manager, true);
        membership.ChangeAuthority(ShopRole.Manager, MembershipStatus.Suspended);
        shop.AddMembership(membership);
        user.AddShopMembership(membership);

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateHandler().HandleAsync(
            new SwitchActiveShopCommand(user.Id, shop.Id, user.SecurityVersion),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
