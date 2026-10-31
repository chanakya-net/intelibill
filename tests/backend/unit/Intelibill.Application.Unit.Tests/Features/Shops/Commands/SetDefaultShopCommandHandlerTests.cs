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
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static (User User, Shop First, Shop Second) TwoShops()
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

        return (user, firstShop, secondShop);
    }

    [Fact]
    public async Task HandleAsync_MultipleShops_SetsRequestedShopAsDefault()
    {
        var (user, firstShop, secondShop) = TwoShops();
        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await new SetDefaultShopCommandHandler(_userRepository, _unitOfWork).HandleAsync(
            new SetDefaultShopCommand(user.Id, secondShop.Id, firstShop.Id),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(secondShop.Id, result.Value.Shops.Single(s => s.IsDefault).ShopId);
        Assert.False(result.Value.Shops.Single(s => s.ShopId == firstShop.Id).IsDefault);
    }

    /// <summary>
    /// Saving a default is a stored preference, not a scope change. The device that saved it stays
    /// in the shop its operator is working in, and gets no new token for having saved it.
    /// </summary>
    [Fact]
    public async Task HandleAsync_LeavesTheRequestingDeviceInTheShopItWasAlreadyIn()
    {
        var (user, firstShop, secondShop) = TwoShops();
        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await new SetDefaultShopCommandHandler(_userRepository, _unitOfWork).HandleAsync(
            new SetDefaultShopCommand(user.Id, secondShop.Id, firstShop.Id),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(firstShop.Id, result.Value.ActiveShopId);
    }

    /// <summary>
    /// Clearing the old default and setting the new one are two writes, because the partial unique
    /// index cannot see both at once. They commit together, so the account is never left with no
    /// default.
    /// </summary>
    [Fact]
    public async Task HandleAsync_CommitsBothWritesAsOneTransaction()
    {
        var (user, firstShop, secondShop) = TwoShops();
        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        _ = await new SetDefaultShopCommandHandler(_userRepository, _unitOfWork).HandleAsync(
            new SetDefaultShopCommand(user.Id, secondShop.Id, firstShop.Id),
            CancellationToken.None);

        await _unitOfWork.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitTransactionAsync(Arg.Any<CancellationToken>());
    }
}
