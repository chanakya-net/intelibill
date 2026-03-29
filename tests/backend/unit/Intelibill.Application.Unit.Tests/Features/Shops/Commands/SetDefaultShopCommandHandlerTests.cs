using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Shops.Commands.SetDefaultShop;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Shops.Commands.SetDefaultShop;

public class SetDefaultShopCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_MultipleShops_SetsRequestedShopAsDefault()
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");

        var firstShop = Shop.Create("First", "Address", "City", "State", "560001", null, null, null);
        var firstMembership = ShopMembership.Create(firstShop.Id, user.Id, ShopRole.Owner, true);
        firstShop.AddMembership(firstMembership);
        user.AddShopMembership(firstMembership);

        var secondShop = Shop.Create("Second", "Address", "City", "State", "560002", null, null, null);
        var secondMembership = ShopMembership.Create(secondShop.Id, user.Id, ShopRole.Manager, false);
        secondShop.AddMembership(secondMembership);
        user.AddShopMembership(secondMembership);

        var command = new SetDefaultShopCommand(user.Id, secondShop.Id);

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);

        _tokenService.GenerateAccessToken(Arg.Any<User>(), Arg.Any<Guid?>())
            .Returns(("access-token", DateTimeOffset.UtcNow.AddMinutes(15)));

        var refreshToken = Domain.Entities.RefreshToken.Create(user.Id, "refresh-token", DateTimeOffset.UtcNow.AddDays(7));
        _tokenService.CreateRefreshToken(user.Id).Returns(refreshToken);

        var handler = new SetDefaultShopCommandHandler(
            _userRepository,
            _refreshTokenRepository,
            _tokenService,
            _unitOfWork);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(secondShop.Id, result.Value.ActiveShopId);
        var shops = result.Value.Shops;
        Assert.NotNull(shops);
        Assert.Equal(secondShop.Id, shops!.Single(s => s.IsDefault).ShopId);
        Assert.False(shops.Single(s => s.ShopId == firstShop.Id).IsDefault);

        await _refreshTokenRepository.Received(1).AddAsync(refreshToken, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}