using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Items.Queries.GetProductDetails;
using ErrorOr;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Items.Queries.GetProductDetails;

public class GetProductDetailsByNameOrBarcodeQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();
    private readonly IInventoryBatchRepository _inventoryBatchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly ISupplierRepository _supplierRepository = Substitute.For<ISupplierRepository>();
    private readonly IExternalProductLookupService _externalProductLookupService = Substitute.For<IExternalProductLookupService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_WhenUserMissing_ReturnsUserNotFound()
    {
        var query = CreateQuery(Guid.NewGuid(), Guid.NewGuid(), productName: "Rice", barcode: null, authorizationHeader: null);
        _userRepository.GetByIdWithDetailsAsync(query.UserId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var sut = CreateSut();
        var result = await sut.HandleAsync(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.UserNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenMembershipMissing_ReturnsMembershipNotFound()
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var query = CreateQuery(user.Id, Guid.NewGuid(), productName: "Rice", barcode: null, authorizationHeader: null);
        _userRepository.GetByIdWithDetailsAsync(query.UserId, Arg.Any<CancellationToken>()).Returns(user);

        var sut = CreateSut();
        var result = await sut.HandleAsync(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenBatchHasActiveSupplier_ReturnsSupplierDetails()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

        var item = Item.Create(shop.Id, "Rice", "Premium Rice", "kg", "111", true, owner.Id);
        var supplier = Supplier.Create(owner.Id, "Acme Foods", null, null, "Street", "City", "State", "560001", true, false);
        var batch = CreateBatch(shop.Id, item.Id, owner.Id, supplier.Id, costPrice: 80m, mrp: 120m, salesPrice: 100m, taxRatePercent: 5m, taxIncluded: true);

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _itemRepository.GetByNameAsync(shop.Id, "Rice", Arg.Any<CancellationToken>()).Returns(item);
        _inventoryBatchRepository.GetByItemAsync(shop.Id, item.Id, Arg.Any<CancellationToken>()).Returns([batch]);
        _supplierRepository.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);

        var sut = CreateSut();
        var result = await sut.HandleAsync(CreateQuery(owner.Id, shop.Id, productName: "Rice", barcode: null, authorizationHeader: null), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Rice", result.Value.Name);
        Assert.Equal("Premium Rice", result.Value.Description);
        Assert.Equal("kg", result.Value.Uom);
        Assert.Equal(80m, result.Value.CostPrice);
        Assert.Equal(120m, result.Value.Mrp);
        Assert.Equal(100m, result.Value.SalesPrice);
        Assert.Equal(supplier.Id, result.Value.SupplierId);
        Assert.Equal("Acme Foods", result.Value.SupplierName);
        Assert.True(result.Value.TaxIncluded);
        Assert.Equal(5m, result.Value.TaxRatePercent);

        await _itemRepository.DidNotReceive().AddAsync(Arg.Any<Item>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _externalProductLookupService.DidNotReceive().LookupByBarcodeAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenBatchHasInactiveSupplier_ReturnsNullSupplierAndTaxMetadata()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

        var item = Item.Create(shop.Id, "Tea", null, "box", "222", true, owner.Id);
        var inactiveSupplier = Supplier.Create(owner.Id, "Old Supplier", null, null, "Street", "City", "State", "560001", false, false);
        var batch = CreateBatch(shop.Id, item.Id, owner.Id, inactiveSupplier.Id, costPrice: 20m, mrp: 30m, salesPrice: 25m, taxRatePercent: 12m, taxIncluded: true);

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _itemRepository.GetByBarcodeAsync(shop.Id, "222", Arg.Any<CancellationToken>()).Returns(item);
        _inventoryBatchRepository.GetByItemAsync(shop.Id, item.Id, Arg.Any<CancellationToken>()).Returns([batch]);
        _supplierRepository.GetByIdAsync(inactiveSupplier.Id, Arg.Any<CancellationToken>()).Returns(inactiveSupplier);

        var sut = CreateSut();
        var result = await sut.HandleAsync(CreateQuery(owner.Id, shop.Id, productName: null, barcode: "222", authorizationHeader: null), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Tea", result.Value.Name);
        Assert.Null(result.Value.SupplierId);
        Assert.Null(result.Value.SupplierName);
        Assert.Null(result.Value.TaxIncluded);
        Assert.Null(result.Value.TaxRatePercent);
    }

    [Fact]
    public async Task HandleAsync_WhenBarcodeMissingInDb_UsesExternalLookup_PersistsItemAndReturnsDefaults()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

        const string barcode = "1234567890123";
        const string authHeader = "Bearer token-123";

        Item? persistedItem = null;

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _itemRepository.GetByBarcodeAsync(shop.Id, barcode, Arg.Any<CancellationToken>()).Returns((Item?)null);
        _externalProductLookupService.LookupByBarcodeAsync(barcode, authHeader, Arg.Any<CancellationToken>())
            .Returns(new ExternalProductLookupResult("Lookup Rice", "From external API", null));
        _itemRepository.AddAsync(Arg.Do<Item>(item => persistedItem = item), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _inventoryBatchRepository.GetByItemAsync(shop.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var sut = CreateSut();
        var result = await sut.HandleAsync(CreateQuery(owner.Id, shop.Id, productName: null, barcode: barcode, authorizationHeader: authHeader), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(persistedItem);
        Assert.Equal("Lookup Rice", persistedItem.Name);
        Assert.Equal(barcode, persistedItem.Barcode);

        Assert.Equal("Lookup Rice", result.Value.Name);
        Assert.Equal("From external API", result.Value.Description);
        Assert.Equal("Unit", result.Value.Uom);
        Assert.Equal(0m, result.Value.CostPrice);
        Assert.Equal(0m, result.Value.Mrp);
        Assert.Equal(0m, result.Value.SalesPrice);
        Assert.Null(result.Value.SupplierId);
        Assert.Null(result.Value.SupplierName);
        Assert.Null(result.Value.TaxIncluded);
        Assert.Null(result.Value.TaxRatePercent);

        await _externalProductLookupService.Received(1).LookupByBarcodeAsync(barcode, authHeader, Arg.Any<CancellationToken>());
        await _itemRepository.Received(1).AddAsync(Arg.Any<Item>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenBarcodeMissingInDb_AndExternalLookupFails_ReturnsNotFound()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _itemRepository.GetByBarcodeAsync(shop.Id, "404", Arg.Any<CancellationToken>()).Returns((Item?)null);
        _externalProductLookupService.LookupByBarcodeAsync("404", "Bearer token-404", Arg.Any<CancellationToken>())
            .Returns((ExternalProductLookupResult?)null);

        var sut = CreateSut();
        var result = await sut.HandleAsync(CreateQuery(owner.Id, shop.Id, productName: null, barcode: "404", authorizationHeader: "Bearer token-404"), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("product.not_found", result.FirstError.Code);

        await _itemRepository.DidNotReceive().AddAsync(Arg.Any<Item>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenExternalLookupReturnsError_PropagatesError()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _itemRepository.GetByBarcodeAsync(shop.Id, "500", Arg.Any<CancellationToken>()).Returns((Item?)null);
        _externalProductLookupService.LookupByBarcodeAsync("500", "Bearer token-500", Arg.Any<CancellationToken>())
            .Returns(Error.Failure("product.lookup.failed", "External lookup failed."));

        var sut = CreateSut();
        var result = await sut.HandleAsync(CreateQuery(owner.Id, shop.Id, productName: null, barcode: "500", authorizationHeader: "Bearer token-500"), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("product.lookup.failed", result.FirstError.Code);
        await _itemRepository.DidNotReceive().AddAsync(Arg.Any<Item>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenExistingItemHasNoBatches_ReturnsNoBatchesError()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

        var item = Item.Create(shop.Id, "Known", null, "kg", "111", true, owner.Id);

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _itemRepository.GetByBarcodeAsync(shop.Id, "111", Arg.Any<CancellationToken>()).Returns(item);
        _inventoryBatchRepository.GetByItemAsync(shop.Id, item.Id, Arg.Any<CancellationToken>()).Returns([]);

        var sut = CreateSut();
        var result = await sut.HandleAsync(CreateQuery(owner.Id, shop.Id, productName: null, barcode: "111", authorizationHeader: "Bearer token"), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("product.no_batches", result.FirstError.Code);
        await _externalProductLookupService.DidNotReceive().LookupByBarcodeAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    private GetProductDetailsByNameOrBarcodeQueryHandler CreateSut() =>
        new(
            _userRepository,
            _itemRepository,
            _inventoryBatchRepository,
            _supplierRepository,
            _externalProductLookupService,
            _unitOfWork);

    private static GetProductDetailsByNameOrBarcodeQuery CreateQuery(
        Guid userId,
        Guid shopId,
        string? productName,
        string? barcode,
        string? authorizationHeader) =>
        new(userId, shopId, productName, barcode, authorizationHeader);

    private static InventoryBatch CreateBatch(
        Guid shopId,
        Guid itemId,
        Guid actorId,
        Guid? supplierId,
        decimal costPrice,
        decimal mrp,
        decimal salesPrice,
        decimal taxRatePercent,
        bool taxIncluded)
    {
        var batchResult = InventoryBatch.Create(
            shopId,
            itemId,
            batchNumber: "B-001",
            quantity: 10m,
            costPrice: costPrice,
            mrp: mrp,
            salesPrice: salesPrice,
            taxRatePercent: taxRatePercent,
            taxIncluded: taxIncluded,
            expiryDate: null,
            manufacturingDate: null,
            supplierId: supplierId,
            createdBy: actorId);

        return batchResult.Value;
    }
}
