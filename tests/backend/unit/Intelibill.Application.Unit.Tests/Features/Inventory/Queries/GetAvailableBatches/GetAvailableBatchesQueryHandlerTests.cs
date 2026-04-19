using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Queries.GetAvailableBatches;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Queries.GetAvailableBatches;

public class GetAvailableBatchesQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly IInventoryBatchRepository _batchRepository = Substitute.For<IInventoryBatchRepository>();

    private GetAvailableBatchesQueryHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _batchRepository);

    private static User MakeUser() =>
        User.CreateWithEmail("inv@test.com", "hash", "Inv", "User");

    private static Shop MakeShop() =>
        Shop.Create("Test Shop", "123 St", "City", "State", "560001", null, null, null);

    private static ShopMembership MakeMembership(Guid shopId, Guid userId) =>
        ShopMembership.Create(shopId, userId, ShopRole.Owner, true);

    private static InventoryBatch MakeBatch(Guid shopId, Guid itemId, string barcode = "BC-001")
    {
        var result = InventoryBatch.Create(shopId, itemId, "B-01",
            50m, 80m, 120m, 100m, 18m, false, null, null, null, Guid.NewGuid());
        return result.Value;
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNotFoundError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();

        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetAvailableBatchesQuery(userId, shopId, "BC-001"), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("User.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenShopNotFound_ReturnsShopNotFoundError()
    {
        var user = MakeUser();
        var shopId = Guid.NewGuid();

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>()).Returns((Shop?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetAvailableBatchesQuery(user.Id, shopId, "BC-001"), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.ShopNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenMembershipNotFound_ReturnsMembershipNotFoundError()
    {
        var user = MakeUser();
        var shop = MakeShop();

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns((ShopMembership?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetAvailableBatchesQuery(user.Id, shop.Id, "BC-001"), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenBatchesExist_ReturnsMappedDtos()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var itemId = Guid.NewGuid();
        var batch = MakeBatch(shop.Id, itemId);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _batchRepository.GetAvailableByBarcodeAsync(shop.Id, "BC-001", Arg.Any<CancellationToken>())
            .Returns(new[] { batch });

        var handler = CreateHandler();
        var result = await handler.Handle(new GetAvailableBatchesQuery(user.Id, shop.Id, "BC-001"), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value);
        Assert.Equal(batch.BatchNumber, result.Value[0].BatchNumber);
        Assert.Equal(batch.Quantity, result.Value[0].Quantity);
        Assert.Equal(batch.SalesPrice, result.Value[0].SalesPrice);
    }
}
