using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Items.Queries.GetProductDetails;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Items.Queries.GetProductDetails;

public class GetProductDetailsByNameOrBarcodeQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();
    private readonly IInventoryBatchRepository _inventoryBatchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly ISupplierRepository _supplierRepository = Substitute.For<ISupplierRepository>();

    private GetProductDetailsByNameOrBarcodeQueryHandler CreateHandler() =>
        new(_userRepository, _itemRepository, _inventoryBatchRepository, _supplierRepository);

    [Fact]
    public async Task HandleAsync_WhenCallerNotInActiveShop_ReturnsForbidden()
    {
        var caller = User.CreateWithEmail("user@test.com", "hash", "User", "One");
        _userRepository.GetByIdWithDetailsAsync(caller.Id, Arg.Any<CancellationToken>()).Returns(caller);

        var result = await CreateHandler().HandleAsync(
            new GetProductDetailsByNameOrBarcodeQuery(caller.Id, Guid.NewGuid(), "Milk", null),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenProductNotFound_ReturnsNotFound()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _itemRepository.GetByNameAsync(shop.Id, "Ghost", Arg.Any<CancellationToken>()).Returns((Item?)null);

        var result = await CreateHandler().HandleAsync(
            new GetProductDetailsByNameOrBarcodeQuery(owner.Id, shop.Id, "Ghost", null),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("product.not_found", result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenNoBatches_ReturnsNotFound()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

        var item = Item.Create(shop.Id, "Milk", null, "ltr", "B001", true, owner.Id);

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _itemRepository.GetByNameAsync(shop.Id, "Milk", Arg.Any<CancellationToken>()).Returns(item);
        _inventoryBatchRepository.GetByItemAsync(shop.Id, item.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<InventoryBatch>)[]);

        var result = await CreateHandler().HandleAsync(
            new GetProductDetailsByNameOrBarcodeQuery(owner.Id, shop.Id, "Milk", null),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("product.no_batches", result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenBatchHasNoSupplier_ReturnsPricingWithNullSupplier()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

        var item = Item.Create(shop.Id, "Milk", "Fresh", "ltr", "B001", true, owner.Id);
        var batchResult = InventoryBatch.Create(
            shop.Id, item.Id, "BN-001", 100m, 42m, 55m, 50m, 5m, taxIncluded: true,
            expiryDate: null, manufacturingDate: null, supplierId: null, createdBy: owner.Id);

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _itemRepository.GetByNameAsync(shop.Id, "Milk", Arg.Any<CancellationToken>()).Returns(item);
        _inventoryBatchRepository.GetByItemAsync(shop.Id, item.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<InventoryBatch>)[batchResult.Value]);

        var result = await CreateHandler().HandleAsync(
            new GetProductDetailsByNameOrBarcodeQuery(owner.Id, shop.Id, "Milk", null),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Fresh", result.Value.Description);
        Assert.Equal("ltr", result.Value.Uom);
        Assert.Equal(42m, result.Value.CostPrice);
        Assert.Equal(55m, result.Value.Mrp);
        Assert.Equal(50m, result.Value.SalesPrice);
        Assert.Null(result.Value.TaxIncluded);
        Assert.Null(result.Value.TaxRatePercent);
        Assert.Null(result.Value.SupplierId);
        Assert.Null(result.Value.SupplierName);
    }

    [Fact]
    public async Task HandleAsync_WhenBatchHasActiveSupplier_ReturnsSupplierDetails()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

        var supplier = Supplier.Create(owner.Id, "Acme Foods", null, null, "Street", "City", "State", "560001", 0m, SupplierStatus.IWillReceive, isActive: true, isPreferred: false);
        var item = Item.Create(shop.Id, "Rice", null, "kg", "B002", true, owner.Id);
        var batchResult = InventoryBatch.Create(
            shop.Id, item.Id, "BN-002", 50m, 80m, 100m, 95m, 18m, taxIncluded: false,
            expiryDate: null, manufacturingDate: null, supplierId: supplier.Id, createdBy: owner.Id);

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _itemRepository.GetByNameAsync(shop.Id, "Rice", Arg.Any<CancellationToken>()).Returns(item);
        _inventoryBatchRepository.GetByItemAsync(shop.Id, item.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<InventoryBatch>)[batchResult.Value]);
        _supplierRepository.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);

        var result = await CreateHandler().HandleAsync(
            new GetProductDetailsByNameOrBarcodeQuery(owner.Id, shop.Id, "Rice", null),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(supplier.Id, result.Value.SupplierId);
        Assert.Equal("Acme Foods", result.Value.SupplierName);
        Assert.False(result.Value.TaxIncluded);
        Assert.Equal(18m, result.Value.TaxRatePercent);
    }

    [Fact]
    public async Task HandleAsync_WhenBatchHasInactiveSupplier_ReturnsNullSupplier()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

        var inactiveSupplier = Supplier.Create(owner.Id, "Old Supplier", null, null, "Street", "City", "State", "560001", 0m, SupplierStatus.IWillReceive, isActive: false, isPreferred: false);
        var item = Item.Create(shop.Id, "Tea", null, "kg", "B003", true, owner.Id);
        var batchResult = InventoryBatch.Create(
            shop.Id, item.Id, "BN-003", 20m, 30m, 40m, 38m, 0m, taxIncluded: false,
            expiryDate: null, manufacturingDate: null, supplierId: inactiveSupplier.Id, createdBy: owner.Id);

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _itemRepository.GetByNameAsync(shop.Id, "Tea", Arg.Any<CancellationToken>()).Returns(item);
        _inventoryBatchRepository.GetByItemAsync(shop.Id, item.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<InventoryBatch>)[batchResult.Value]);
        _supplierRepository.GetByIdAsync(inactiveSupplier.Id, Arg.Any<CancellationToken>()).Returns(inactiveSupplier);

        var result = await CreateHandler().HandleAsync(
            new GetProductDetailsByNameOrBarcodeQuery(owner.Id, shop.Id, "Tea", null),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Null(result.Value.SupplierId);
        Assert.Null(result.Value.SupplierName);
        Assert.Null(result.Value.TaxIncluded);
        Assert.Null(result.Value.TaxRatePercent);
    }
}
