using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Shops.Commands.SwitchActiveShop;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Shops.Commands.SwitchActiveShop;

public class SwitchActiveShopCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_WhenTargetMembershipIsManager_ReturnsOwnerOnlyError()
    {
        var user = User.CreateWithEmail("manager@test.com", "hash", "Manager", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var membership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Manager, true);
        shop.AddMembership(membership);
        user.AddShopMembership(membership);

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var handler = new SwitchActiveShopCommandHandler(_userRepository, _refreshTokenRepository, _tokenService, _unitOfWork);
        var result = await handler.HandleAsync(new SwitchActiveShopCommand(user.Id, shop.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.UserIsNotOwnerForSwitch.Code, result.FirstError.Code);
    }
}
