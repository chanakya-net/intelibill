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

    private static InventoryBatch MakeBatch(Guid shopId, Guid itemId)
    {
        var result = InventoryBatch.Create(shopId, itemId, "B-01",
            50m, 80m, 120m, 100m, 18m, false, null, null, null, Guid.NewGuid());
        return result.Value;
    }

    private static Item MakeItem(Guid shopId, string barcode, string name) =>
        Item.Create(shopId, name, null, "kg", barcode, true, Guid.NewGuid());

    private static InventoryBatch MakeBatchWithItem(Guid shopId, Item item)
    {
        var batch = MakeBatch(shopId, item.Id);
        typeof(InventoryBatch).GetProperty(nameof(InventoryBatch.Item), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)!
            .SetValue(batch, item);
        return batch;
    }

    private static string CreateQrLikeBarcode() =>
        $"QR|01|{Guid.NewGuid():N}|TRACE|{Guid.NewGuid():N}|PAYLOAD|{new string('B', 24)}";

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNotFoundError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var searchTerm = CreateQrLikeBarcode();

        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetAvailableBatchesQuery(userId, shopId, searchTerm), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("User.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenShopNotFound_ReturnsShopNotFoundError()
    {
        var user = MakeUser();
        var shopId = Guid.NewGuid();
        var searchTerm = CreateQrLikeBarcode();

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>()).Returns((Shop?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetAvailableBatchesQuery(user.Id, shopId, searchTerm), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.ShopNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenMembershipNotFound_ReturnsMembershipNotFoundError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var searchTerm = CreateQrLikeBarcode();

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns((ShopMembership?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetAvailableBatchesQuery(user.Id, shop.Id, searchTerm), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenSearchTermUsed_CallsSearchRepositoryAndMapsItemFields()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var searchTerm = "apple";
        var item = MakeItem(shop.Id, "BAR-1", "Green Apple");
        var batch = MakeBatchWithItem(shop.Id, item);
        var expectedBatchId = batch.Id;

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _batchRepository.SearchAvailableByProductNameOrBatchNumberAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>())
            .Returns(new[] { batch });

        var handler = CreateHandler();
        var result = await handler.Handle(new GetAvailableBatchesQuery(user.Id, shop.Id, searchTerm), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value);
        Assert.Equal(item.Barcode, result.Value[0].Barcode);
        Assert.Equal(item.Name, result.Value[0].ItemName);
        Assert.Equal(batch.BatchNumber, result.Value[0].BatchNumber);
        Assert.Equal(expectedBatchId, result.Value[0].InventoryBatchId);
        Assert.Equal(batch.Quantity, result.Value[0].Quantity);
        Assert.Equal(batch.SalesPrice, result.Value[0].SalesPrice);

        await _batchRepository.Received(1).SearchAvailableByProductNameOrBatchNumberAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>());
        await _batchRepository.DidNotReceive().GetAvailableByBarcodeAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenBarcodeUsed_CallsBarcodeRepositoryAndMapsItemFields()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var searchTerm = CreateQrLikeBarcode();
        var item = MakeItem(shop.Id, "BAR-QR", "QR Rice");
        var batch = MakeBatchWithItem(shop.Id, item);
        var expectedBatchId = batch.Id;

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _batchRepository.GetAvailableByBarcodeAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>())
            .Returns(new[] { batch });

        var handler = CreateHandler();
        var result = await handler.Handle(new GetAvailableBatchesQuery(user.Id, shop.Id, searchTerm, true), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value);
        Assert.Equal(item.Barcode, result.Value[0].Barcode);
        Assert.Equal(item.Name, result.Value[0].ItemName);
        Assert.Equal(batch.BatchNumber, result.Value[0].BatchNumber);
        Assert.Equal(expectedBatchId, result.Value[0].InventoryBatchId);
        Assert.Equal(batch.Quantity, result.Value[0].Quantity);
        Assert.Equal(batch.SalesPrice, result.Value[0].SalesPrice);

        await _batchRepository.Received(1).GetAvailableByBarcodeAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>());
        await _batchRepository.DidNotReceive().SearchAvailableByProductNameOrBatchNumberAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
