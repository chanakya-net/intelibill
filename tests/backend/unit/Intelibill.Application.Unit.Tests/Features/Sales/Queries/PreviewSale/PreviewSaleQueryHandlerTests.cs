using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.Queries.PreviewSale;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Application.Features.Sales.Services.Pricing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Queries.PreviewSale;

public class PreviewSaleQueryHandlerTests
{
    private readonly ISaleLineValidator _saleLineValidator = Substitute.For<ISaleLineValidator>();
    private readonly ISalePricingCalculator _pricingCalculator = Substitute.For<ISalePricingCalculator>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();

    private PreviewSaleQueryHandler CreateHandler() => new(_userRepository, _shopRepository, _saleLineValidator, _pricingCalculator);

    private static Item MakeItem(Guid shopId, string barcode, string name = "Rice") =>
        Item.Create(shopId, name, "desc", "kg", barcode, true, Guid.NewGuid());

    private static InventoryBatch MakeBatch(Guid shopId, Guid itemId, string batchNumber, decimal quantity = 100m, bool voided = false)
    {
        var result = InventoryBatch.Create(shopId, itemId, batchNumber,
            quantity, costPrice: 80m, mrp: 120m, salesPrice: 100m,
            taxRatePercent: 18m, taxIncluded: false, expiryDate: null,
            manufacturingDate: null, supplierId: null, createdBy: Guid.NewGuid());

        var batch = result.Value;
        if (voided)
            batch.Void(Guid.NewGuid());
        return batch;
    }

    private static Domain.Entities.Inventory MakeInventory(Guid shopId, Guid itemId, decimal quantity = 100m)
    {
        var result = Domain.Entities.Inventory.Create(shopId, itemId, quantity, 10m, 500m, Guid.NewGuid());
        return result.Value;
    }

    private static User MakeUser() =>
        User.CreateWithEmail("test@example.com", "hash", "Test", "User");

    private static Shop MakeShop() =>
        Shop.Create("Main Shop", "Addr", "City", "State", "123456", null, null, null);

    private static PreviewSaleQuery MakeQuery(
        Guid userId,
        Guid shopId,
        Guid batchId,
        decimal quantity = 1m,
        bool mismatch = false,
        InstantDiscountType saleDiscountType = InstantDiscountType.None,
        decimal saleDiscountValue = 0m,
        InstantDiscountType itemDiscountType = InstantDiscountType.None,
        decimal itemDiscountValue = 0m)
    {
        var costPrice = mismatch ? 70m : 80m;
        return new PreviewSaleQuery(
            userId,
            shopId,
            new InstantDiscount(saleDiscountType, saleDiscountValue),
            [
                new PreviewSaleLineQuery(
                    batchId,
                    "BC-001",
                    "B-01",
                    "Rice",
                    quantity,
                    costPrice,
                    100m,
                    120m,
                    18m,
                    false,
                    new InstantDiscount(itemDiscountType, itemDiscountValue),
                    "line-1"),
            ]);
    }

    [Fact]
    public async Task Handle_WhenNoItems_ReturnsItemsRequired()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(new PreviewSaleQuery(Guid.NewGuid(), Guid.NewGuid(), new InstantDiscount(InstantDiscountType.None, 0m), []), CancellationToken.None);
        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.ItemsRequired.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenActorUserNotFound_ReturnsUserNotFound()
    {
        var query = MakeQuery(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _userRepository.GetByIdAsync(query.ActorUserId, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("User.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenShopNotFound_ReturnsShopNotFound()
    {
        var query = MakeQuery(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _userRepository.GetByIdAsync(query.ActorUserId, Arg.Any<CancellationToken>())
            .Returns(MakeUser());
        _shopRepository.GetByIdAsync(query.ShopId, Arg.Any<CancellationToken>())
            .Returns((Shop?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.ShopNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenMembershipMissing_ReturnsMembershipNotFound()
    {
        var query = MakeQuery(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _userRepository.GetByIdAsync(query.ActorUserId, Arg.Any<CancellationToken>())
            .Returns(MakeUser());
        _shopRepository.GetByIdAsync(query.ShopId, Arg.Any<CancellationToken>())
            .Returns(MakeShop());
        _shopRepository.GetMembershipAsync(query.ActorUserId, query.ShopId, Arg.Any<CancellationToken>())
            .Returns((ShopMembership?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenSuccessful_ReturnsPreviewAndDoesNotMutateBatchOrInventory()
    {
        var shopId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01", quantity: 100m);
        var inventory = MakeInventory(shopId, item.Id, quantity: 100m);

        var query = MakeQuery(userId, shopId, batch.Id);
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(MakeUser());
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(MakeShop());
        _shopRepository.GetMembershipAsync(userId, shopId, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shopId, userId, ShopRole.Owner, true));
        var cmdItem = new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 80m, 100m, 120m, 18m, false, batch.Id, ClientLineKey: "line-1");
        var validated = new ValidatedSaleLine(cmdItem, item, batch, inventory, HasPriceMismatch: false);
        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SaleLineValidationResult>>(
                new SaleLineValidationResult([validated], new Dictionary<Guid, string> { { item.Id, item.Name } })));

        _pricingCalculator.CalculateAsync(Arg.Any<SalePricingCalculationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SalePricingCalculationResult>>(
                new SalePricingCalculationResult(
                    [
                        new SalePricingLineCalculation(
                            batch.Id, 1m, 80m, 100m, 18m, false,
                            PreTaxAmountBeforeDiscount: 100m,
                            ItemDiscountAmount: 0m,
                            SaleDiscountAmount: 0m,
                            TaxableAmount: 100m,
                            TaxAmount: 18m,
                            TotalAmount: 118m,
                            MaxAllowedItemDiscountFlat: 10m,
                            MaxAllowedItemDiscountPercent: 10m,
                            ConfiguredBatchRuleId: null,
                            ConfiguredBatchRulePercentage: null),
                    ],
                    SaleLevelEligibleSubtotal: 100m,
                    TotalTaxableAmount: 100m,
                    TotalTaxAmount: 18m,
                    TotalDiscountAmount: 0m,
                    TotalAmount: 118m,
                    ConfiguredSaleRule: null,
                    Infos: [])));

        var handler = CreateHandler();
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(118m, result.Value.TotalAmount);
        Assert.Single(result.Value.Lines);
        Assert.Equal("line-1", result.Value.Lines[0].ClientLineKey);
        Assert.Equal(100m, batch.Quantity);
        Assert.Equal(100m, inventory.Quantity);
    }

    [Fact]
    public async Task Handle_WhenInsufficientStock_ReturnsError()
    {
        _saleLineValidator.ValidateLinesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(),
                Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SaleLineValidationResult>>(Errors.Sale.InsufficientStock("BC-001", "B-01")));
        _userRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(MakeUser());
        _shopRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(MakeShop());
        _shopRepository.GetMembershipAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(Guid.NewGuid(), Guid.NewGuid(), ShopRole.Owner, true));

        var handler = CreateHandler();
        var result = await handler.Handle(MakeQuery(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.InsufficientStock("BC-001", "B-01").Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenBatchVoided_ReturnsError()
    {
        _saleLineValidator.ValidateLinesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(),
                Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SaleLineValidationResult>>(Errors.Sale.BatchVoided("BC-001", "B-01")));
        _userRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(MakeUser());
        _shopRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(MakeShop());
        _shopRepository.GetMembershipAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(Guid.NewGuid(), Guid.NewGuid(), ShopRole.Owner, true));

        var handler = CreateHandler();
        var result = await handler.Handle(MakeQuery(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.BatchVoided("BC-001", "B-01").Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenClientPriceMismatch_IncludesWarning()
    {
        var shopId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01", quantity: 100m);
        var inventory = MakeInventory(shopId, item.Id, quantity: 100m);

        var query = MakeQuery(userId, shopId, batch.Id, mismatch: true);
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(MakeUser());
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(MakeShop());
        _shopRepository.GetMembershipAsync(userId, shopId, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shopId, userId, ShopRole.Owner, true));
        var cmdItem = new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 70m, 100m, 120m, 18m, false, batch.Id, ClientLineKey: "line-1");
        var validated = new ValidatedSaleLine(cmdItem, item, batch, inventory, HasPriceMismatch: true);

        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SaleLineValidationResult>>(
                new SaleLineValidationResult([validated], new Dictionary<Guid, string> { { item.Id, item.Name } })));

        _pricingCalculator.CalculateAsync(Arg.Any<SalePricingCalculationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SalePricingCalculationResult>>(
                new SalePricingCalculationResult(
                    [
                        new SalePricingLineCalculation(
                            batch.Id, 1m, 80m, 100m, 18m, false,
                            100m, 0m, 0m, 100m, 18m, 118m, 10m, 10m, null, null),
                    ],
                    100m, 100m, 18m, 0m, 118m, null, [])));

        var handler = CreateHandler();
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains(result.Value.Warnings, w => w.Code == "sale_preview.warning.client_price_mismatch" && w.ClientLineKey == "line-1");
    }

    [Fact]
    public async Task Handle_WhenTaxOverrideProvided_UsesOverrideForPricing()
    {
        var shopId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01", quantity: 100m);
        var inventory = MakeInventory(shopId, item.Id, quantity: 100m);

        var query = new PreviewSaleQuery(
            userId,
            shopId,
            new InstantDiscount(InstantDiscountType.None, 0m),
            [
                new PreviewSaleLineQuery(
                    batch.Id,
                    "BC-001",
                    "B-01",
                    "Rice",
                    1m,
                    80m,
                    100m,
                    120m,
                    5m,
                    false,
                    new InstantDiscount(InstantDiscountType.None, 0m),
                    "line-1"),
            ]);

        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(MakeUser());
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(MakeShop());
        _shopRepository.GetMembershipAsync(userId, shopId, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shopId, userId, ShopRole.Owner, true));

        var cmdItem = new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 80m, 100m, 120m, 5m, false, batch.Id, ClientLineKey: "line-1");
        var validated = new ValidatedSaleLine(cmdItem, item, batch, inventory, HasPriceMismatch: false);
        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SaleLineValidationResult>>(
                new SaleLineValidationResult([validated], new Dictionary<Guid, string> { { item.Id, item.Name } })));

        SalePricingCalculationRequest? capturedRequest = null;
        _pricingCalculator.CalculateAsync(Arg.Any<SalePricingCalculationRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedRequest = callInfo.ArgAt<SalePricingCalculationRequest>(0);
                return Task.FromResult<ErrorOr<SalePricingCalculationResult>>(
                    new SalePricingCalculationResult(
                        [
                            new SalePricingLineCalculation(
                                batch.Id, 1m, 80m, 100m, 5m, batch.TaxIncluded,
                                100m, 0m, 0m, 100m, 5m, 105m, 10m, 10m, null, null),
                        ],
                        100m, 100m, 5m, 0m, 105m, null, []));
            });

        var handler = CreateHandler();
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(capturedRequest);
        Assert.Equal(5m, capturedRequest!.Lines[0].TaxRatePercent);
        Assert.Equal(batch.TaxIncluded, capturedRequest.Lines[0].IsPriceIncludingTax);
    }

    [Fact]
    public async Task Handle_WhenBelowCostInstantDiscount_ReturnsError()
    {
        var shopId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var query = MakeQuery(userId, shopId, batchId, itemDiscountType: InstantDiscountType.Flat, itemDiscountValue: 999m);
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(MakeUser());
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(MakeShop());
        _shopRepository.GetMembershipAsync(userId, shopId, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shopId, userId, ShopRole.Owner, true));

        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SaleLineValidationResult>>(
                new SaleLineValidationResult([], new Dictionary<Guid, string>())));

        _pricingCalculator.CalculateAsync(Arg.Any<SalePricingCalculationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SalePricingCalculationResult>>(Errors.Sale.ItemDiscountWouldBeBelowCost(batchId)));

        var handler = CreateHandler();
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.ItemDiscountWouldBeBelowCost(batchId).Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenNoEligibleSaleLevelLines_InfoIsReturned()
    {
        var shopId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01", quantity: 100m);
        var inventory = MakeInventory(shopId, item.Id, quantity: 100m);
        var query = MakeQuery(userId, shopId, batch.Id);
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(MakeUser());
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(MakeShop());
        _shopRepository.GetMembershipAsync(userId, shopId, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shopId, userId, ShopRole.Owner, true));

        var cmdItem = new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 80m, 50m, 120m, 0m, false, batch.Id, ClientLineKey: "line-1");
        var validated = new ValidatedSaleLine(cmdItem, item, batch, inventory, HasPriceMismatch: false);
        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SaleLineValidationResult>>(
                new SaleLineValidationResult([validated], new Dictionary<Guid, string> { { item.Id, item.Name } })));

        _pricingCalculator.CalculateAsync(Arg.Any<SalePricingCalculationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SalePricingCalculationResult>>(
                new SalePricingCalculationResult(
                    [
                        new SalePricingLineCalculation(
                            batch.Id, 1m, 80m, 50m, 0m, false,
                            50m, 0m, 0m, 50m, 0m, 50m, 0m, 0m, null, null),
                    ],
                    SaleLevelEligibleSubtotal: 0m,
                    TotalTaxableAmount: 50m,
                    TotalTaxAmount: 0m,
                    TotalDiscountAmount: 0m,
                    TotalAmount: 50m,
                    ConfiguredSaleRule: null,
                    Infos: [new SalePricingInfoMessage(
                        "sale_pricing.info.no_eligible_lines_for_configured_sale_discount",
                        "No eligible lines.")])));

        var handler = CreateHandler();
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains(result.Value.Infos, i => i.Code == "sale_pricing.info.no_eligible_lines_for_configured_sale_discount");
    }

    [Fact]
    public async Task Handle_WhenDuplicateBatchLinesAndNullClientLineKey_PreservesLineDiscountMapping()
    {
        var shopId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01", quantity: 100m);
        var inventory = MakeInventory(shopId, item.Id, quantity: 100m);

        var query = new PreviewSaleQuery(
            userId,
            shopId,
            new InstantDiscount(InstantDiscountType.None, 0m),
            [
                new PreviewSaleLineQuery(
                    batch.Id, "BC-001", "B-01", "Rice", 1m, 80m, 100m, 120m, 18m, false,
                    new InstantDiscount(InstantDiscountType.Flat, 5m), null),
                new PreviewSaleLineQuery(
                    batch.Id, "BC-001", "B-01", "Rice", 2m, 80m, 100m, 120m, 18m, false,
                    new InstantDiscount(InstantDiscountType.Percentage, 10m), "line-2"),
            ]);
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(MakeUser());
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(MakeShop());
        _shopRepository.GetMembershipAsync(userId, shopId, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shopId, userId, ShopRole.Owner, true));

        var validatedLines = new List<ValidatedSaleLine>
        {
            new(
                new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 80m, 100m, 120m, 18m, false, batch.Id, null),
                item,
                batch,
                inventory,
                HasPriceMismatch: false),
            new(
                new RecordSaleItemCommand("BC-001", "B-01", "Rice", 2m, 80m, 100m, 120m, 18m, false, batch.Id, ClientLineKey: "line-2"),
                item,
                batch,
                inventory,
                HasPriceMismatch: false),
        };

        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SaleLineValidationResult>>(
                new SaleLineValidationResult(validatedLines, new Dictionary<Guid, string> { { item.Id, item.Name } })));

        SalePricingCalculationRequest? capturedRequest = null;
        _pricingCalculator.CalculateAsync(Arg.Any<SalePricingCalculationRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedRequest = callInfo.ArgAt<SalePricingCalculationRequest>(0);
                return Task.FromResult<ErrorOr<SalePricingCalculationResult>>(
                    new SalePricingCalculationResult(
                        [
                            new SalePricingLineCalculation(batch.Id, 1m, 80m, 100m, 18m, false, 100m, 5m, 0m, 95m, 17.1m, 112.1m, 20m, 20m, null, null),
                            new SalePricingLineCalculation(batch.Id, 2m, 80m, 100m, 18m, false, 200m, 20m, 0m, 180m, 32.4m, 212.4m, 40m, 20m, null, null),
                        ],
                        SaleLevelEligibleSubtotal: 275m,
                        TotalTaxableAmount: 275m,
                        TotalTaxAmount: 49.5m,
                        TotalDiscountAmount: 25m,
                        TotalAmount: 324.5m,
                        ConfiguredSaleRule: null,
                        Infos: []));
            });

        var handler = CreateHandler();
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(capturedRequest);
        Assert.Equal(2, capturedRequest!.Lines.Count);
        Assert.Equal(InstantDiscountType.Flat, capturedRequest.Lines[0].ItemDiscount.Type);
        Assert.Equal(5m, capturedRequest.Lines[0].ItemDiscount.Value);
        Assert.Equal(InstantDiscountType.Percentage, capturedRequest.Lines[1].ItemDiscount.Type);
        Assert.Equal(10m, capturedRequest.Lines[1].ItemDiscount.Value);
        Assert.Null(result.Value.Lines[0].ClientLineKey);
        Assert.Equal("line-2", result.Value.Lines[1].ClientLineKey);
    }
}
