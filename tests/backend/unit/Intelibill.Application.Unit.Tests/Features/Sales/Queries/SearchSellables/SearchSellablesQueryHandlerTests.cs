using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Queries.SearchSellables;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Queries.SearchSellables;

public sealed class SearchSellablesQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly IInventoryBatchRepository _inventoryBatchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IServiceRepository _serviceRepository = Substitute.For<IServiceRepository>();

    private SearchSellablesQueryHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _inventoryBatchRepository, _serviceRepository);

    private static User MakeUser() =>
        User.CreateWithEmail("billing@test.com", "hash", "Billing", "User");

    private static Shop MakeShop() =>
        Shop.Create("Test Shop", "123 Main St", "City", "State", "560001", null, null, null);

    private static ShopMembership MakeMembership(Guid shopId, Guid userId) =>
        ShopMembership.Create(shopId, userId, ShopRole.Owner, true);

    private static Item MakeItem(Guid shopId, string barcode, string name) =>
        Item.Create(shopId, name, null, "kg", barcode, true, Guid.NewGuid());

    private static InventoryBatch MakeBatchWithItem(Guid shopId, Item item, string batchNumber = "B-01")
    {
        var batch = InventoryBatch.Create(
            shopId,
            item.Id,
            batchNumber,
            5m,
            80m,
            120m,
            100m,
            18m,
            false,
            null,
            null,
            null,
            Guid.NewGuid()).Value;

        typeof(InventoryBatch).GetProperty(nameof(InventoryBatch.Item), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)!
            .SetValue(batch, item);

        return batch;
    }

    private static Service MakeService(Guid shopId, string code, string name, bool isActive = true) =>
        Service.Create(shopId, code, name, "Service description", 250m, "9988", 18m, false, isActive, Guid.NewGuid());

    private static string CreateQrLikeBarcode() =>
        $"QR|01|{Guid.NewGuid():N}|TRACE|{Guid.NewGuid():N}|PAYLOAD|{new string('B', 24)}";

    [Fact]
    public async Task Handle_WhenGoodsAndServicesMatch_ReturnsMixedSellables()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var searchTerm = "Rice";
        var item = MakeItem(shop.Id, "BAR-001", "Rice Flour");
        var batch = MakeBatchWithItem(shop.Id, item);
        var service = MakeService(shop.Id, "SVC-001", "Rice Packaging");

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _inventoryBatchRepository.SearchAvailableByProductNameOrBatchNumberAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>()).Returns([batch]);
        _serviceRepository.SearchActiveAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>()).Returns([service]);

        var result = await CreateHandler().HandleAsync(new SearchSellablesQuery(user.Id, shop.Id, searchTerm), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(SellableKind.Goods, result.Value[0].Kind);
        Assert.Equal(batch.Id, result.Value[0].InventoryBatchId);
        Assert.Equal(SellableKind.Service, result.Value[1].Kind);
        Assert.Equal(service.Id, result.Value[1].ServiceId);

        await _inventoryBatchRepository.Received(1).SearchAvailableByProductNameOrBatchNumberAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>());
        await _inventoryBatchRepository.DidNotReceive().GetAvailableByBarcodeAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _serviceRepository.Received(1).SearchActiveAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenOnlyGoodsMatch_ReturnsGoodsOnly()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var searchTerm = "Milk";
        var item = MakeItem(shop.Id, "barcode-123", "Milk");
        var batch = MakeBatchWithItem(shop.Id, item);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _inventoryBatchRepository.SearchAvailableByProductNameOrBatchNumberAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>()).Returns([batch]);
        _serviceRepository.SearchActiveAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateHandler().HandleAsync(new SearchSellablesQuery(user.Id, shop.Id, searchTerm), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value);
        var sellable = result.Value[0];
        Assert.Equal(SellableKind.Goods, sellable.Kind);
        Assert.Equal(item.Barcode, sellable.Barcode);
        Assert.Equal(batch.BatchNumber, sellable.BatchNumber);
        Assert.Equal(batch.Id, sellable.InventoryBatchId);
        Assert.Null(sellable.ServiceId);

        await _inventoryBatchRepository.Received(1).SearchAvailableByProductNameOrBatchNumberAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>());
        await _inventoryBatchRepository.DidNotReceive().GetAvailableByBarcodeAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenOnlyServicesMatch_ReturnsServicesOnly()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var searchTerm = "consult";
        var service = MakeService(shop.Id, "SVC-010", "Consulting");

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _inventoryBatchRepository.SearchAvailableByProductNameOrBatchNumberAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>()).Returns([]);
        _serviceRepository.SearchActiveAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>()).Returns([service]);

        var result = await CreateHandler().HandleAsync(new SearchSellablesQuery(user.Id, shop.Id, searchTerm), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value);
        var sellable = result.Value[0];
        Assert.Equal(SellableKind.Service, sellable.Kind);
        Assert.Equal(service.Id, sellable.ServiceId);
        Assert.Equal(service.Code, sellable.Code);
        Assert.Equal(service.Name, sellable.Name);
        Assert.Equal(service.Price, sellable.Price);
        Assert.Null(sellable.InventoryBatchId);

        await _inventoryBatchRepository.Received(1).SearchAvailableByProductNameOrBatchNumberAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>());
        await _inventoryBatchRepository.DidNotReceive().GetAvailableByBarcodeAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenBarcodeLookupRequested_UsesBarcodeSearchAndSkipsTextSearch()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var searchTerm = CreateQrLikeBarcode();
        var item = MakeItem(shop.Id, "BAR-QR", "QR Rice");
        var batch = MakeBatchWithItem(shop.Id, item);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _inventoryBatchRepository.GetAvailableByBarcodeAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>()).Returns([batch]);
        _serviceRepository.SearchActiveAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateHandler().HandleAsync(new SearchSellablesQuery(user.Id, shop.Id, searchTerm, true), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value);
        Assert.Equal(SellableKind.Goods, result.Value[0].Kind);
        Assert.Equal(batch.Id, result.Value[0].InventoryBatchId);

        await _inventoryBatchRepository.Received(1).GetAvailableByBarcodeAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>());
        await _inventoryBatchRepository.DidNotReceive().SearchAvailableByProductNameOrBatchNumberAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenBarcodeLookup_ServiceSearchIsNotCalled()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var searchTerm = CreateQrLikeBarcode();
        var item = MakeItem(shop.Id, "BAR-QR", "QR Rice");
        var batch = MakeBatchWithItem(shop.Id, item);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _inventoryBatchRepository.GetAvailableByBarcodeAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>()).Returns([batch]);

        var result = await CreateHandler().HandleAsync(new SearchSellablesQuery(user.Id, shop.Id, searchTerm, true), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value);

        await _serviceRepository.DidNotReceive().SearchActiveAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenInactiveServiceExistsInShop_IsNotReturned()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var searchTerm = "consulting";

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _inventoryBatchRepository.SearchAvailableByProductNameOrBatchNumberAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>()).Returns([]);
        _serviceRepository.SearchActiveAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateHandler().HandleAsync(new SearchSellablesQuery(user.Id, shop.Id, searchTerm), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(result.Value);
        Assert.DoesNotContain(result.Value, sellable => sellable.Kind == SellableKind.Service);
        await _serviceRepository.Received(1).SearchActiveAsync(shop.Id, searchTerm, Arg.Any<CancellationToken>());
    }
}
