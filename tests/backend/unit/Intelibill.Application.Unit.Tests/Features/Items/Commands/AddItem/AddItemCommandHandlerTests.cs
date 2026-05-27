using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Items.Commands.AddItem;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Events;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Items.Commands.AddItem;

public class AddItemCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_WhenBarcodeExists_ReturnsConflict()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var existing = Item.Create(shop.Id, "Existing", null, "kg", "111", true, actor.Id);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByBarcodeAsync(shop.Id, "111", Arg.Any<CancellationToken>()).Returns(existing);

        var handler = new AddItemCommandHandler(_userRepository, _itemRepository, _unitOfWork);
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Item.BarcodeAlreadyExists.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenValid_CreatesItem()
    {
        var actor = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByBarcodeAsync(shop.Id, "111", Arg.Any<CancellationToken>()).Returns((Item?)null);
        _itemRepository.GetByNameAsync(shop.Id, "Rice", Arg.Any<CancellationToken>()).Returns((Item?)null);

        var handler = new AddItemCommandHandler(_userRepository, _itemRepository, _unitOfWork);
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Rice", result.Value.Name);
        Assert.Equal("111", result.Value.Barcode);
        Assert.Equal("10063090", result.Value.HsnCode);
        Assert.Equal(5m, result.Value.DefaultTaxRatePercent);
        Assert.False(result.Value.DefaultTaxIncluded);
        Assert.Equal(0m, result.Value.CurrentStock);
        Assert.Null(result.Value.UnitPrice);
        Assert.Equal(0m, result.Value.CurrentStockValue);
        Assert.Equal(0m, result.Value.ReorderLevel);
        Assert.Equal("outOfStock", result.Value.StockStatus);

        await _itemRepository.Received(1).AddAsync(Arg.Is<Item>(i =>
            i.Name == "Rice" &&
            i.Barcode == "111" &&
            i.HsnCode == "10063090" &&
            i.DefaultTaxRatePercent == 5m &&
            !i.DefaultTaxIncluded), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenValid_CreatedItemHasItemCreatedDomainEvent()
    {
        var actor = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true));

        Item? capturedItem = null;
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByBarcodeAsync(shop.Id, "111", Arg.Any<CancellationToken>()).Returns((Item?)null);
        _itemRepository.GetByNameAsync(shop.Id, "Rice", Arg.Any<CancellationToken>()).Returns((Item?)null);
        await _itemRepository.AddAsync(Arg.Do<Item>(i => capturedItem = i), Arg.Any<CancellationToken>());

        var handler = new AddItemCommandHandler(_userRepository, _itemRepository, _unitOfWork);
        await handler.HandleAsync(CreateCommand(actor.Id, shop.Id), CancellationToken.None);

        Assert.NotNull(capturedItem);
        var domainEvent = Assert.Single(capturedItem.DomainEvents);
        var created = Assert.IsType<ItemCreatedDomainEvent>(domainEvent);
        Assert.Equal(capturedItem.Id, created.ItemId);
        Assert.Equal("111", created.Barcode);
        Assert.Equal("Rice", created.Name);
        Assert.Equal(shop.Id, created.ShopId);
    }

    private static AddItemCommand CreateCommand(Guid actorId, Guid shopId) =>
        new(
            actorId,
            shopId,
            Name: "Rice",
            Barcode: "111",
            Description: "Premium",
            Uom: "kg",
            IsActive: true,
            HsnCode: "10063090",
            DefaultTaxRatePercent: 5m);
}
