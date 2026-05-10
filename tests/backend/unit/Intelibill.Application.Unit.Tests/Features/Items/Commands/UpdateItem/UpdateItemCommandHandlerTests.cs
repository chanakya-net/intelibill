using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Items.Commands.UpdateItem;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Events;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;
using Wolverine;

namespace Intelibill.Application.Unit.Tests.Features.Items.Commands.UpdateItem;

public class UpdateItemCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMessageBus _messageBus = Substitute.For<IMessageBus>();

    [Fact]
    public async Task HandleAsync_WhenItemNotFound_ReturnsNotFound()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByIdAsync(Guid.NewGuid(), Arg.Any<CancellationToken>()).Returns((Item?)null);

        var handler = new UpdateItemCommandHandler(_userRepository, _itemRepository, _unitOfWork, _messageBus);
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Item.ItemNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenUserIsNotOwnerOrManager_ReturnsForbidden()
    {
        var actor = User.CreateWithEmail("staff@test.com", "hash", "Staff", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Staff, true));

        var item = Item.Create(shop.Id, "Rice", null, "kg", "111", true, actor.Id);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByIdAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);

        var handler = new UpdateItemCommandHandler(_userRepository, _itemRepository, _unitOfWork, _messageBus);
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id, item.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Item.UserIsNotOwnerOrManager.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenBarcodeAlreadyExists_ReturnsConflict()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var item = Item.Create(shop.Id, "Rice", null, "kg", "111", true, actor.Id);
        var existing = Item.Create(shop.Id, "Wheat", null, "kg", "222", true, actor.Id);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByIdAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);
        _itemRepository.GetByBarcodeAsync(shop.Id, "222", Arg.Any<CancellationToken>()).Returns(existing);

        var handler = new UpdateItemCommandHandler(_userRepository, _itemRepository, _unitOfWork, _messageBus);
        var result = await handler.HandleAsync(
            CreateCommand(actor.Id, shop.Id, item.Id, barcode: "222"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Item.BarcodeAlreadyExists.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenBarcodeUnchanged_AllowsUpdate()
    {
        var actor = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true));

        var item = Item.Create(shop.Id, "Rice", null, "kg", "111", true, actor.Id);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByIdAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);

        var handler = new UpdateItemCommandHandler(_userRepository, _itemRepository, _unitOfWork, _messageBus);
        var result = await handler.HandleAsync(
            CreateCommand(actor.Id, shop.Id, item.Id, name: "Updated Rice", barcode: "111"),
            CancellationToken.None);

        Assert.False(result.IsError);
        _itemRepository.Received(1).Update(Arg.Is<Item>(i => i.Name == "Updated Rice"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _messageBus.Received(1).PublishAsync(
            Arg.Is<ItemApplicabilityChangedDomainEvent>(@event =>
                @event.ShopId == shop.Id &&
                @event.ItemId == item.Id));
    }

    [Fact]
    public async Task HandleAsync_WhenValid_UpdatesItem()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var item = Item.Create(shop.Id, "Rice", null, "kg", "111", true, actor.Id);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByIdAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);
        _itemRepository.GetByBarcodeAsync(shop.Id, "222", Arg.Any<CancellationToken>()).Returns((Item?)null);

        var handler = new UpdateItemCommandHandler(_userRepository, _itemRepository, _unitOfWork, _messageBus);
        var result = await handler.HandleAsync(
            CreateCommand(actor.Id, shop.Id, item.Id, name: "Premium Rice", description: "Best Quality", barcode: "222", uom: "kg"),
            CancellationToken.None);

        Assert.False(result.IsError);
        _itemRepository.Received(1).Update(Arg.Is<Item>(i =>
            i.Name == "Premium Rice" &&
            i.Barcode == "222" &&
            i.Description == "Best Quality" &&
            i.UpdatedBy == actor.Id));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _messageBus.Received(1).PublishAsync(
            Arg.Is<ItemApplicabilityChangedDomainEvent>(@event =>
                @event.ShopId == shop.Id &&
                @event.ItemId == item.Id));
    }

    [Fact]
    public async Task HandleAsync_WhenItemFromDifferentShop_ReturnsNotFound()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop1 = Shop.Create("Shop1", "Address", "City", "State", "560001", null, null, null);
        var shop2 = Shop.Create("Shop2", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop1.Id, actor.Id, ShopRole.Owner, true));

        var item = Item.Create(shop2.Id, "Rice", null, "kg", "111", true, actor.Id);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByIdAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);

        var handler = new UpdateItemCommandHandler(_userRepository, _itemRepository, _unitOfWork, _messageBus);
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop1.Id, item.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Item.ItemNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenItemIsDeactivated_PublishesApplicabilityEvent()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var item = Item.Create(shop.Id, "Rice", null, "kg", "111", true, actor.Id);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByIdAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);

        var handler = new UpdateItemCommandHandler(_userRepository, _itemRepository, _unitOfWork, _messageBus);
        var result = await handler.HandleAsync(
            CreateCommand(
                actor.Id,
                shop.Id,
                item.Id,
                name: "Rice",
                barcode: "111",
                description: item.Description,
                uom: "kg",
                isActive: false),
            CancellationToken.None);

        Assert.False(result.IsError);
        await _messageBus.Received(1).PublishAsync(
            Arg.Is<ItemApplicabilityChangedDomainEvent>(@event =>
                @event.ShopId == shop.Id &&
                @event.ItemId == item.Id));
    }

    [Fact]
    public async Task HandleAsync_WhenOnlyDescriptionChanges_DoesNotPublishApplicabilityEvent()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var item = Item.Create(shop.Id, "Rice", null, "kg", "111", true, actor.Id);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByIdAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);

        var handler = new UpdateItemCommandHandler(_userRepository, _itemRepository, _unitOfWork, _messageBus);
        var result = await handler.HandleAsync(
            CreateCommand(
                actor.Id,
                shop.Id,
                item.Id,
                name: "Rice",
                barcode: "111",
                description: "Updated description",
                uom: "kg"),
            CancellationToken.None);

        Assert.False(result.IsError);
        await _messageBus.DidNotReceive().PublishAsync(Arg.Any<ItemApplicabilityChangedDomainEvent>());
    }

    private static UpdateItemCommand CreateCommand(
        Guid actorId,
        Guid shopId,
        Guid? itemId = null,
        string name = "Updated Rice",
        string barcode = "112",
        string? description = "Premium",
        string uom = "kg",
        bool? isActive = null) =>
        new(
            actorId,
            shopId,
            itemId ?? Guid.NewGuid(),
            name,
            barcode,
            description,
            uom,
            isActive);
}
