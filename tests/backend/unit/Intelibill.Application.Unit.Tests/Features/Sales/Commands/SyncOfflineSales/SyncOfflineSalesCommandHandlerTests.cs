using System.Globalization;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Commands.SyncOfflineSales;

public class SyncOfflineSalesCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IInvoiceLeaseRepository _invoiceLeaseRepository = Substitute.For<IInvoiceLeaseRepository>();
    private readonly ISaleLineValidator _saleLineValidator = Substitute.For<ISaleLineValidator>();
    private readonly ICustomerResolver _customerResolver = Substitute.For<ICustomerResolver>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly ICustomerLedgerEntryRepository _customerLedgerEntryRepository = Substitute.For<ICustomerLedgerEntryRepository>();
    private readonly IStockTransactionRepository _stockTransactionRepository = Substitute.For<IStockTransactionRepository>();
    private readonly IReconciliationIssueRepository _reconciliationIssueRepository = Substitute.For<IReconciliationIssueRepository>();
    private readonly IDiscountRuleRepository _discountRuleRepository = Substitute.For<IDiscountRuleRepository>();
    private readonly IOfflineSaleVarianceAnalyzer _varianceAnalyzer;
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly IOfflineSubmissionRepository _offlineSubmissionRepository = Substitute.For<IOfflineSubmissionRepository>();
    private readonly List<OfflineSubmission> _recordedSubmissions = [];
    private readonly FixedClock _clock = new(Now);

    // Fiscal year 2025-26 (1 April 2025 to 31 March 2026), matching the leases' "INV-2025-26-" prefix.
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    public SyncOfflineSalesCommandHandlerTests()
    {
        _varianceAnalyzer = new OfflineSaleVarianceAnalyzer(_reconciliationIssueRepository);
        StubRecordedEnvelope(candidate => candidate);
        _customerResolver.ResolveAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid?>(),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                Arg.Any<PaymentMethod>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Customer?>>((Customer?)null));
        _discountRuleRepository.GetActiveByShopAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DiscountRule>>([]));
    }

    private SyncOfflineSalesCommandHandler CreateHandler() =>
        new(
            _userRepository,
            _shopRepository,
            _invoiceLeaseRepository,
            _offlineSubmissionRepository,
            _saleRepository,
            _discountRuleRepository,
            _varianceAnalyzer,
            new OfflineSalePoster(
                _saleLineValidator,
                _customerResolver,
                _saleRepository,
                _customerLedgerEntryRepository,
                _stockTransactionRepository,
                _shopRepository,
                _varianceAnalyzer),
            _unitOfWork,
            _clock);

    private static User CreateMemberUser(Guid shopId)
    {
        var user = User.CreateWithEmail("user@test.com", "hash", "Test", "User");
        user.AddShopMembership(ShopMembership.Create(shopId, user.Id, ShopRole.Owner, true));
        return user;
    }

    private static InvoiceLease CreateLease(
        Guid shopId,
        string deviceId = "device-1",
        int rangeStart = 1,
        int rangeEnd = 50,
        DateTimeOffset? reservedAt = null,
        DateTimeOffset? expiresAt = null) =>
        InvoiceLease.Create(
            shopId,
            Guid.NewGuid(),
            deviceId,
            2025,
            "INV-2025-26-",
            rangeStart,
            rangeEnd,
            6,
            reservedAt ?? Now.AddDays(-1),
            expiresAt ?? Now.AddDays(6));

    private static Item CreateItem(Guid shopId, string barcode = "BC-001", string name = "Rice") =>
        Item.Create(shopId, name, "desc", "kg", barcode, true, Guid.NewGuid());

    private static InventoryBatch CreateBatch(
        Guid shopId,
        Guid itemId,
        string batchNumber = "B-01",
        decimal quantity = 100m,
        decimal salesPrice = 100m,
        decimal mrp = 120m)
    {
        return InventoryBatch.Create(
            shopId,
            itemId,
            batchNumber,
            quantity,
            costPrice: 80m,
            mrp: mrp,
            salesPrice: salesPrice,
            taxRatePercent: 18m,
            taxIncluded: false,
            expiryDate: null,
            manufacturingDate: null,
            supplierId: null,
            createdBy: Guid.NewGuid()).Value;
    }

    private static Domain.Entities.Inventory CreateInventory(Guid shopId, Guid itemId, decimal quantity = 100m) =>
        Domain.Entities.Inventory.Create(shopId, itemId, quantity, 10m, 500m, Guid.NewGuid()).Value;

    private static OfflineSaleSyncLineCommand CreateLine(
        Guid? inventoryBatchId = null,
        string barcode = "BC-001",
        string batchNumber = "B-01",
        decimal quantity = 1m) =>
        new(
            barcode,
            batchNumber,
            "Rice",
            quantity,
            80m,
            100m,
            120m,
            18m,
            false,
            inventoryBatchId ?? Guid.NewGuid(),
            100m * quantity,
            0m,
            0m,
            100m * quantity,
            18m * quantity,
            118m * quantity,
            null,
            null,
            InstantDiscountType.None,
            0m,
            null);

    private static OfflineSaleSyncCommand CreateSale(
        string clientSaleId,
        string invoiceNumber = "INV-2025-26-000001",
        PaymentMethod paymentMethod = PaymentMethod.Cash,
        decimal paidAmount = 118m,
        decimal dueAmount = 0m,
        Guid? customerId = null,
        string? customerPhone = "+919876543210",
        IReadOnlyList<OfflineSaleSyncLineCommand>? items = null)
    {
        var effectiveItems = items ?? [CreateLine()];
        var total = effectiveItems.Sum(x => x.TotalAmount);
        var tax = effectiveItems.Sum(x => x.TaxAmount);
        var subtotal = effectiveItems.Sum(x => x.PreTaxAmountBeforeDiscount);
        var discount = Math.Max(0m, effectiveItems.Sum(x => x.ItemDiscountAmount + x.SaleDiscountAmount));

        return new OfflineSaleSyncCommand(
            clientSaleId,
            invoiceNumber,
            Now.AddMinutes(-5),
            customerId,
            "Ravi",
            customerPhone,
            paymentMethod,
            paidAmount,
            dueAmount,
            subtotal,
            total + discount,
            discount,
            tax,
            total,
            InstantDiscountType.None,
            0m,
            null,
            null,
            null,
            null,
            effectiveItems);
    }

    private static void SetupSuccessfulLineValidation(
        ISaleLineValidator validator,
        Guid shopId,
        Item item,
        InventoryBatch batch,
        Domain.Entities.Inventory inventory)
    {
        validator.ValidateLinesAsync(
                shopId,
                Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(),
                Arg.Any<List<string>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
            .Returns(callInfo =>
            {
                var commands = callInfo.ArgAt<IReadOnlyList<RecordSaleItemCommand>>(1);
                var warnings = callInfo.ArgAt<List<string>>(2);
                var lines = commands
                    .Select(command => new ValidatedSaleLine(
                        command,
                        item,
                        batch,
                        inventory,
                        command.CostPrice != batch.CostPrice
                        || command.SalesPrice != batch.SalesPrice
                        || command.Mrp != batch.Mrp
                        || command.TaxRatePercent != batch.TaxRatePercent
                        || command.IsPriceIncludingTax != batch.TaxIncluded))
                    .ToList();

                foreach (var command in commands)
                {
                    if (!string.Equals(command.ItemName.Trim(), item.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add($"Item name mismatch for barcode '{command.Barcode}': provided '{command.ItemName}', found '{item.Name}'.");
                    }
                }

                return Task.FromResult<ErrorOr<SaleLineValidationResult>>(
                    new SaleLineValidationResult(lines, new Dictionary<Guid, string> { [item.Id] = item.Name }));
            });
    }

    [Fact]
    public async Task HandleAsync_WhenOfflineLineFractionsAWholeUnitItem_FailsWithQuantityPrecisionExceeded()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var item = Item.Create(shopId, "Rice", "desc", "PCS", "BC-001", true, Guid.NewGuid());
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var sale = CreateSale(
            $"offline-{Guid.NewGuid():N}",
            paidAmount: 59m,
            items: [CreateLine(batch.Id, quantity: 0.5m)]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);

        var itemRepository = Substitute.For<IItemRepository>();
        var batchRepository = Substitute.For<IInventoryBatchRepository>();
        var inventoryRepository = Substitute.For<IInventoryRepository>();
        batchRepository.GetByIdWithItemAsync(batch.Id, shopId, Arg.Any<CancellationToken>()).Returns(batch);
        itemRepository.GetByIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns([item]);
        inventoryRepository.GetByItemIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns([inventory]);
        var realValidator = new SaleLineValidator(itemRepository, batchRepository, inventoryRepository);
        _saleLineValidator.ValidateLinesAsync(
                shopId,
                Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(),
                Arg.Any<List<string>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
            .Returns(call => realValidator.ValidateLinesAsync(
                call.ArgAt<Guid>(0),
                call.ArgAt<IReadOnlyList<RecordSaleItemCommand>>(1),
                call.ArgAt<List<string>>(2),
                call.ArgAt<CancellationToken>(3),
                call.ArgAt<bool>(4)));
        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("failed", syncResult.Status);
        Assert.Contains(syncResult.Errors, error => error.Code == "Sale.QuantityPrecisionExceeded");
        await _saleRepository.DidNotReceive().AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenOfflineLineAmountIsNegative_ReturnsFailedResult()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var sale = CreateSale(
            $"offline-{Guid.NewGuid():N}",
            items: [CreateLine(batch.Id) with { ItemDiscountAmount = -1m }]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("failed", syncResult.Status);
        Assert.Contains(syncResult.Errors, error => error.Code == Errors.Sale.OfflineLineAmountsInvalid.Code);
        await _saleLineValidator.DidNotReceive()
            .ValidateLinesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>());
        await _saleRepository.DidNotReceive().AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenStockIsShort_CreatesSaleConsumesAvailableAndPersistsStockVariance()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id, quantity: 1m);
        var inventory = CreateInventory(shopId, item.Id, quantity: 1m);
        var sale = CreateSale(
            $"offline-{Guid.NewGuid():N}",
            paidAmount: 354m,
            items: [CreateLine(batch.Id, quantity: 3m)]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("SyncedWithWarnings", syncResult.Status);
        Assert.Contains(syncResult.Warnings, warning => warning.Contains("stock", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0m, batch.Quantity);
        Assert.Equal(0m, inventory.Quantity);
        await _stockTransactionRepository.Received(1).AddAsync(
            Arg.Is<StockTransaction>(tx => tx.Quantity == -1m),
            Arg.Any<CancellationToken>());
        await _saleRepository.Received(1).AddAsync(
            Arg.Is<Sale>(s => s.Items.Single().Quantity == 3m),
            Arg.Any<CancellationToken>());
        await _reconciliationIssueRepository.Received(1).AddAsync(
            Arg.Is<ReconciliationIssue>(issue =>
                issue.ShopId == shopId
                && issue.ClientSaleId == sale.ClientSaleId
                && issue.DeviceId == deviceId
                && issue.IssueType == ReconciliationIssueType.StockVariance
                && issue.PrintedQuantity == 3m
                && issue.AvailableQuantity == 1m
                && issue.ConsumedQuantity == 1m
                && !issue.IsResolved),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCustomerInactive_CreatesSaleWithWarningAndCustomerVarianceIssue()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var customer = Customer.Create(shopId, "Current Name", "+910000000000", null, isActive: false);
        var sale = CreateSale(
            $"offline-{Guid.NewGuid():N}",
            customerId: customer.Id,
            customerPhone: "+919876543210",
            items: [CreateLine(batch.Id)]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));
        _customerResolver.ResolveAsync(shopId, customer.Id, sale.CustomerPhone, false, PaymentMethod.Cash, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Customer?>>(customer));
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("SyncedWithWarnings", syncResult.Status);
        Assert.Contains(syncResult.Warnings, warning => warning.Contains("customer", StringComparison.OrdinalIgnoreCase));
        await _saleRepository.Received(1).AddAsync(
            Arg.Is<Sale>(s => s.CustomerId == customer.Id && s.CustomerName == sale.CustomerName && s.CustomerPhone == sale.CustomerPhone),
            Arg.Any<CancellationToken>());
        await _reconciliationIssueRepository.Received().AddAsync(
            Arg.Is<ReconciliationIssue>(issue =>
                issue.IssueType == ReconciliationIssueType.CustomerVariance
                && issue.ClientSaleId == sale.ClientSaleId
                && !issue.IsResolved),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenPricingDiffers_CreatesSaleWithPricingVarianceIssue()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var sale = CreateSale(
            $"offline-{Guid.NewGuid():N}",
            paidAmount: 413m,
            items: [CreateLine(batch.Id) with { SalesPrice = 350m, TotalAmount = 413m, PreTaxAmountBeforeDiscount = 350m, TaxableAmount = 350m, TaxAmount = 63m }]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("SyncedWithWarnings", syncResult.Status);
        await _saleRepository.Received(1).AddAsync(
            Arg.Is<Sale>(s => s.Items.Single().SalesPrice == 350m && s.TotalAmount == 413m),
            Arg.Any<CancellationToken>());
        await _reconciliationIssueRepository.Received(1).AddAsync(
            Arg.Is<ReconciliationIssue>(issue => issue.IssueType == ReconciliationIssueType.PricingVariance),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenValidatorEmitsWarningOnlyVariance_PersistsValidationIssue()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var sale = CreateSale(
            $"offline-{Guid.NewGuid():N}",
            items: [CreateLine(batch.Id) with { ItemName = "Printed Rice" }]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("SyncedWithWarnings", syncResult.Status);
        Assert.Contains(syncResult.Warnings, warning => warning.Contains("Item name mismatch", StringComparison.Ordinal));
        await _saleRepository.Received(1).AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        await _reconciliationIssueRepository.Received(1).AddAsync(
            Arg.Is<ReconciliationIssue>(issue =>
                issue.IssueType == ReconciliationIssueType.ValidationConflict
                && issue.Code == "offline_sync.validation_variance"
                && issue.ClientSaleId == sale.ClientSaleId
                && issue.SaleId.HasValue
                && !issue.IsResolved),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenDiscountRuleChanged_CreatesSaleWithDiscountVarianceIssue()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var configuredRuleId = Guid.NewGuid();
        var sale = CreateSale(
            $"offline-{Guid.NewGuid():N}",
            paidAmount: 106.2m,
            items:
            [
                CreateLine(batch.Id) with
                {
                    ConfiguredBatchRuleId = configuredRuleId,
                    ConfiguredBatchRulePercentage = 10m,
                    ItemDiscountAmount = 10m,
                    TaxableAmount = 90m,
                    TaxAmount = 16.2m,
                    TotalAmount = 106.2m,
                },
            ]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));
        _discountRuleRepository.GetActiveByShopAsync(shopId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("SyncedWithWarnings", syncResult.Status);
        await _saleRepository.Received(1).AddAsync(
            Arg.Is<Sale>(s => s.Items.Single().ConfiguredBatchRuleId == configuredRuleId && s.TotalAmount == 106.2m),
            Arg.Any<CancellationToken>());
        await _reconciliationIssueRepository.Received(1).AddAsync(
            Arg.Is<ReconciliationIssue>(issue => issue.IssueType == ReconciliationIssueType.DiscountVariance),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenInvoiceAlreadyUsedByDifferentSale_ReturnsNeedsReviewAndPersistsInvoiceConflict()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", items: [CreateLine()]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);
        var existingSale = Sale.Create(
            shopId,
            actorId,
            "other-key",
            "other-hash",
            sale.InvoiceNumber,
            null,
            null,
            null,
            PaymentMethod.Cash,
            sale.SoldAt,
            118m,
            0m,
            118m,
            18m,
            [SaleItem.CreateGoods(shopId, Guid.NewGuid(), Guid.NewGuid(), lineName: "Item", lineCode: "BC-001", 1m, 80m, 100m, 120m, 18m, false, false)]);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));
        _saleRepository.GetByInvoiceNumberAsync(shopId, sale.InvoiceNumber, Arg.Any<CancellationToken>())
            .Returns(existingSale);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("NeedsReview", syncResult.Status);
        Assert.Contains(syncResult.Errors, error => error.Code == Errors.Sale.InvoiceNumberAlreadyUsed.Code);
        await _saleRepository.DidNotReceive().AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        await _reconciliationIssueRepository.Received(1).AddAsync(
            Arg.Is<ReconciliationIssue>(issue => issue.IssueType == ReconciliationIssueType.InvoiceConflict && issue.SaleId == null),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenInvoiceOutsideActiveLeaseRange_ReturnsNeedsReviewAndPersistsInvoiceConflict()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var sale = CreateSale(
            $"offline-{Guid.NewGuid():N}",
            invoiceNumber: "INV-2025-26-000099",
            items: [CreateLine()]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId, rangeStart: 1, rangeEnd: 50));

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("NeedsReview", syncResult.Status);
        Assert.Contains(syncResult.Errors, error => error.Code == Errors.Sale.InvoiceLeaseNotFound.Code);
        await _saleRepository.DidNotReceive().AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        await _reconciliationIssueRepository.Received(1).AddAsync(
            Arg.Is<ReconciliationIssue>(issue =>
                issue.IssueType == ReconciliationIssueType.InvoiceConflict
                && issue.Code == Errors.Sale.InvoiceLeaseNotFound.Code
                && issue.SaleId == null
                && issue.ClientSaleId == sale.ClientSaleId),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenNoLeaseMatches_ReturnsNeedsReviewAndPersistsInvoiceConflict()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", items: [CreateLine()]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("NeedsReview", syncResult.Status);
        Assert.Contains(syncResult.Errors, error => error.Code == Errors.Sale.InvoiceLeaseNotFound.Code);
        await _saleRepository.DidNotReceive().AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        await _reconciliationIssueRepository.Received(1).AddAsync(
            Arg.Is<ReconciliationIssue>(issue =>
                issue.IssueType == ReconciliationIssueType.InvoiceConflict
                && issue.Code == Errors.Sale.InvoiceLeaseNotFound.Code
                && issue.SaleId == null
                && issue.ClientSaleId == sale.ClientSaleId),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HeldAgainForALegacyCase_AdoptsItInsteadOfFilingAnother()
    {
        // Held before envelopes existed (no lease matched then either): the resend's envelope now evidences that case.
        var (shopId, actorId, deviceId) = (Guid.NewGuid(), Guid.NewGuid(), "device-1");
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", items: [CreateLine()]);
        var legacy = ReconciliationIssue.Create(
            shopId, null, sale.ClientSaleId, deviceId, ReconciliationIssueType.InvoiceConflict,
            Errors.Sale.InvoiceLeaseNotFound.Code, Errors.Sale.InvoiceLeaseNotFound.Description, actorId);
        var issues = CaptureReconciliationIssues();
        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>()).Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId);
        _reconciliationIssueRepository
            .FindOpenLegacyIssueAsync(
                shopId, sale.ClientSaleId, deviceId, ReconciliationIssueType.InvoiceConflict,
                Errors.Sale.InvoiceLeaseNotFound.Code, Errors.Sale.InvoiceLeaseNotFound.Description, Arg.Any<CancellationToken>())
            .Returns(legacy);

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]), CancellationToken.None);

        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("NeedsReview", syncResult.Status);
        Assert.Equal(legacy.Id, syncResult.ReviewCaseId);
        Assert.Empty(issues);
        Assert.Equal(Assert.Single(_recordedSubmissions).Id, legacy.OfflineSubmissionId);
        Assert.Equal(sale.InvoiceNumber, legacy.InvoiceNumber);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HeldForAnEnvelopeWithItsOwnCase_NeverAdoptsALegacyCase()
    {
        // Per-envelope reuse comes first; a legacy case is adopted only when the envelope has none.
        var (shopId, actorId, deviceId) = (Guid.NewGuid(), Guid.NewGuid(), "device-1");
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", items: [CreateLine()]);
        var ownCaseId = Guid.NewGuid();
        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>()).Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId);
        _reconciliationIssueRepository
            .FindOpenIssueIdAsync(shopId, sale.ClientSaleId, deviceId, ReconciliationIssueType.InvoiceConflict, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(ownCaseId);

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]), CancellationToken.None);

        Assert.Equal(ownCaseId, Assert.Single(result.Value.Results).ReviewCaseId);
        await _reconciliationIssueRepository.DidNotReceive().FindOpenLegacyIssueAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ReconciliationIssueType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenInvoiceLeaseExpired_ReturnsNeedsReviewForHistoricalLease()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", items: [CreateLine()]) with { SoldAt = Now.AddDays(-5) };
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId, reservedAt: Now.AddDays(-10), expiresAt: Now.AddDays(-3)));

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("NeedsReview", syncResult.Status);
        Assert.Contains(syncResult.Errors, error => error.Code == Errors.OfflineReconciliation.HistoricalLeaseReviewRequired.Code);
        await _saleRepository.DidNotReceive().AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        await _reconciliationIssueRepository.Received(1).AddAsync(
            Arg.Is<ReconciliationIssue>(issue =>
                issue.IssueType == ReconciliationIssueType.IssueTimingReview
                && issue.Code == Errors.OfflineReconciliation.HistoricalLeaseReviewRequired.Code
                && issue.SaleId == null
                && issue.ClientSaleId == sale.ClientSaleId),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenClientSalePayloadDiffers_ReturnsNeedsReviewAndPersistsValidationConflict()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", items: [CreateLine()]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);
        var existingSale = Sale.Create(
            shopId,
            actorId,
            OfflineSaleSyncIdempotencyHasher.ComputeKey(deviceId, sale.ClientSaleId),
            "DIFFERENT-HASH",
            sale.InvoiceNumber,
            null,
            sale.CustomerName,
            sale.CustomerPhone,
            sale.PaymentMethod,
            sale.SoldAt,
            sale.PaidAmount,
            sale.DueAmount,
            sale.TotalAmount,
            sale.TotalTaxAmount,
            [SaleItem.CreateGoods(shopId, Guid.NewGuid(), Guid.NewGuid(), lineName: "Item", lineCode: "BC-001", 1m, 80m, 100m, 120m, 18m, false, false)],
            source: SaleSource.Offline,
            clientSaleId: sale.ClientSaleId,
            deviceId: deviceId,
            syncedAt: DateTimeOffset.UtcNow);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        _saleRepository.GetByClientSaleIdAsync(shopId, deviceId, sale.ClientSaleId, Arg.Any<CancellationToken>())
            .Returns(existingSale);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("NeedsReview", syncResult.Status);
        Assert.Contains(syncResult.Errors, error => error.Code == Errors.Sale.IdempotencyConflict.Code);
        await _reconciliationIssueRepository.Received(1).AddAsync(
            Arg.Is<ReconciliationIssue>(issue => issue.IssueType == ReconciliationIssueType.ValidationConflict && issue.SaleId == existingSale.Id),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenDuplicate_ReturnsExistingResult()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";

        var user = User.CreateWithEmail("user@test.com", "hash", "Test", "User");
        user.AddShopMembership(ShopMembership.Create(shopId, user.Id, ShopRole.Owner, true));

        var line = new OfflineSaleSyncLineCommand(
            "BC-001",
            "B-01",
            "Rice",
            1m,
            80m,
            100m,
            120m,
            18m,
            false,
            Guid.NewGuid(),
            100m,
            0m,
            0m,
            100m,
            18m,
            118m,
            null,
            null,
            InstantDiscountType.None,
            0m,
            null);

        var sale = new OfflineSaleSyncCommand(
            $"offline-{Guid.NewGuid():N}",
            "INV-2025-26-000001",
            DateTimeOffset.UtcNow,
            null,
            "Ravi",
            "+919876543210",
            PaymentMethod.Cash,
            118m,
            0m,
            100m,
            118m,
            0m,
            18m,
            118m,
            InstantDiscountType.None,
            0m,
            null,
            null,
            null,
            null,
            [line]);

        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);
        var requestHash = OfflineSaleSyncIdempotencyHasher.ComputeHash(shopId, deviceId, sale);
        var persistedWarnings = new[] { "Offline stock shortage was reconciled." };

        var saleItem = SaleItem.CreateGoods(
            shopId,
            Guid.NewGuid(),
            line.InventoryBatchId,
            lineName: line.ItemName,
            lineCode: line.Barcode,
            line.Quantity,
            line.CostPrice,
            line.SalesPrice,
            line.Mrp,
            line.TaxRatePercent,
            line.IsPriceIncludingTax,
            false,
            preTaxAmountBeforeDiscount: line.PreTaxAmountBeforeDiscount,
            itemDiscountAmount: line.ItemDiscountAmount,
            saleDiscountAmount: line.SaleDiscountAmount,
            taxableAmount: line.TaxableAmount,
            taxAmount: line.TaxAmount,
            totalAmount: line.TotalAmount,
            configuredBatchRuleId: line.ConfiguredBatchRuleId,
            configuredBatchRulePercentage: line.ConfiguredBatchRulePercentage,
            itemDiscountOverrideType: line.ItemDiscountOverrideType,
            itemDiscountOverrideValue: line.ItemDiscountOverrideValue,
            hsnCode: line.HsnCode);

        var existingSale = Sale.Create(
            shopId,
            actorId,
            OfflineSaleSyncIdempotencyHasher.ComputeKey(deviceId, sale.ClientSaleId),
            requestHash,
            sale.InvoiceNumber,
            sale.CustomerId,
            sale.CustomerName,
            sale.CustomerPhone,
            sale.PaymentMethod,
            sale.SoldAt,
            sale.PaidAmount,
            sale.DueAmount,
            sale.TotalAmount,
            sale.TotalTaxAmount,
            [saleItem],
            subtotalBeforeDiscount: sale.SubtotalBeforeDiscount,
            totalBeforeDiscount: sale.TotalBeforeDiscount,
            totalDiscountAmount: sale.TotalDiscountAmount,
            configuredSaleRuleId: sale.ConfiguredSaleRuleId,
            configuredSaleRuleType: sale.ConfiguredSaleRuleType,
            configuredSaleRulePercentage: sale.ConfiguredSaleRulePercentage,
            configuredSaleRuleThresholdAmount: sale.ConfiguredSaleRuleThresholdAmount,
            saleDiscountOverrideType: sale.SaleDiscountOverrideType,
            saleDiscountOverrideValue: sale.SaleDiscountOverrideValue,
            source: SaleSource.Offline,
            clientSaleId: sale.ClientSaleId,
            deviceId: deviceId,
            syncedAt: DateTimeOffset.UtcNow,
            warnings: persistedWarnings);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(user);
        StubLeases(shopId, deviceId);
        _saleRepository.GetByClientSaleIdAsync(shopId, deviceId, sale.ClientSaleId, Arg.Any<CancellationToken>())
            .Returns(existingSale);

        // The first sync stored the envelope and linked it to the sale; the resend returns that row.
        StubRecordedEnvelope(candidate =>
        {
            var stored = FirstReceipt(candidate, Now.AddDays(-1));
            stored.LinkSale(existingSale.Id);
            return stored;
        });

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var item = Assert.Single(result.Value.Results);
        Assert.Equal("duplicate", item.Status);
        Assert.Equal(existingSale.Id, item.SaleId);
        Assert.Equal(persistedWarnings, item.Warnings);
        await _saleLineValidator.DidNotReceive()
            .ValidateLinesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenOneSaleHasPayloadError_ContinuesBatch()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var invalidSale = CreateSale($"offline-{Guid.NewGuid():N}", invoiceNumber: "   ");
        var validSale = CreateSale(
            $"offline-{Guid.NewGuid():N}",
            invoiceNumber: "INV-2025-26-000001",
            items: [CreateLine(batch.Id)]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [invalidSale, validSale]);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Collection(
            result.Value.Results,
            first =>
            {
                Assert.Equal(invalidSale.ClientSaleId, first.ClientSaleId);
                Assert.Equal("failed", first.Status);
                Assert.Contains(first.Errors, error => error.Code == Errors.Sale.InvoiceNumberRequired.Code);
            },
            second =>
            {
                Assert.Equal(validSale.ClientSaleId, second.ClientSaleId);
                Assert.Equal("created", second.Status);
            });
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenOfflineLinesAreIdentical_CreatesBothLines()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var line = CreateLine(batch.Id);
        var sale = CreateSale(
            $"offline-{Guid.NewGuid():N}",
            paidAmount: 236m,
            items: [line, line]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("created", Assert.Single(result.Value.Results).Status);
        await _saleRepository.Received(1).AddAsync(
            Arg.Is<Sale>(s => s.Items.Count == 2 && s.TotalAmount == 236m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenDueSaleResolvesCustomer_AddsCustomerLedgerEntry()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var customer = Customer.Create(shopId, "Due Customer", "+919876543210", null);
        var sale = CreateSale(
            $"offline-{Guid.NewGuid():N}",
            paymentMethod: PaymentMethod.Credit,
            paidAmount: 0m,
            dueAmount: 118m,
            customerId: customer.Id,
            items: [CreateLine(batch.Id)]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));
        _customerResolver.ResolveAsync(shopId, customer.Id, sale.CustomerPhone, true, PaymentMethod.Credit, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Customer?>>(customer));
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("SyncedWithWarnings", Assert.Single(result.Value.Results).Status);
        await _customerLedgerEntryRepository.Received(1).AddAsync(
            Arg.Is<CustomerLedgerEntry>(entry =>
                entry.CustomerId == customer.Id
                && entry.EntryType == CustomerLedgerEntryType.SaleDue
                && entry.Amount == 118m),
            Arg.Any<CancellationToken>());
        await _saleRepository.Received(1).AddAsync(
            Arg.Is<Sale>(s => s.CustomerId == customer.Id && s.CustomerName == sale.CustomerName),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenUniqueClientSaleRaceMatches_ReturnsDuplicateResult()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", items: [CreateLine(batch.Id)]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);
        var requestHash = OfflineSaleSyncIdempotencyHasher.ComputeHash(shopId, deviceId, sale);
        var persistedWarnings = new[] { "Offline pricing variance was reconciled." };
        var concurrentSale = Sale.Create(
            shopId,
            actorId,
            OfflineSaleSyncIdempotencyHasher.ComputeKey(deviceId, sale.ClientSaleId),
            requestHash,
            sale.InvoiceNumber,
            null,
            sale.CustomerName,
            sale.CustomerPhone,
            sale.PaymentMethod,
            sale.SoldAt,
            sale.PaidAmount,
            sale.DueAmount,
            sale.TotalAmount,
            sale.TotalTaxAmount,
            [SaleItem.CreateGoods(shopId, item.Id, batch.Id, lineName: item.Name, lineCode: item.Barcode, 1m, 80m, 100m, 120m, 18m, false, false)],
            source: SaleSource.Offline,
            clientSaleId: sale.ClientSaleId,
            deviceId: deviceId,
            syncedAt: DateTimeOffset.UtcNow,
            warnings: persistedWarnings);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));
        _saleRepository.GetByClientSaleIdAsync(shopId, deviceId, sale.ClientSaleId, Arg.Any<CancellationToken>())
            .Returns((Sale?)null, concurrentSale);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new DbUpdateException("unique race")), Task.FromResult(1));
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("duplicate", syncResult.Status);
        Assert.Equal(concurrentSale.Id, syncResult.SaleId);
        Assert.Equal(persistedWarnings, syncResult.Warnings);
        await _saleRepository.Received(2).GetByClientSaleIdAsync(shopId, deviceId, sale.ClientSaleId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenUniqueClientSaleRaceDiffers_ReturnsConflictResult()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", items: [CreateLine(batch.Id)]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);
        var concurrentSale = Sale.Create(
            shopId,
            actorId,
            OfflineSaleSyncIdempotencyHasher.ComputeKey(deviceId, sale.ClientSaleId),
            "DIFFERENT-HASH",
            sale.InvoiceNumber,
            null,
            sale.CustomerName,
            sale.CustomerPhone,
            sale.PaymentMethod,
            sale.SoldAt,
            sale.PaidAmount,
            sale.DueAmount,
            sale.TotalAmount,
            sale.TotalTaxAmount,
            [SaleItem.CreateGoods(shopId, item.Id, batch.Id, lineName: item.Name, lineCode: item.Barcode, 1m, 80m, 100m, 120m, 18m, false, false)],
            source: SaleSource.Offline,
            clientSaleId: sale.ClientSaleId,
            deviceId: deviceId,
            syncedAt: DateTimeOffset.UtcNow);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));
        _saleRepository.GetByClientSaleIdAsync(shopId, deviceId, sale.ClientSaleId, Arg.Any<CancellationToken>())
            .Returns((Sale?)null, concurrentSale);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new DbUpdateException("unique race")), Task.FromResult(1));
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("NeedsReview", syncResult.Status);
        Assert.Contains(syncResult.Errors, error => error.Code == Errors.Sale.IdempotencyConflict.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenRaceFailsFirstSale_ClearsChangesAndContinuesBatch()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id, quantity: 1m);
        var inventory = CreateInventory(shopId, item.Id, quantity: 1m);
        var firstSale = CreateSale(
            $"offline-{Guid.NewGuid():N}",
            invoiceNumber: "INV-2025-26-000001",
            items: [CreateLine(batch.Id)]);
        var secondSale = CreateSale(
            $"offline-{Guid.NewGuid():N}",
            invoiceNumber: "INV-2025-26-000002",
            items: [CreateLine(batch.Id)]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [firstSale, secondSale]);
        var requestHash = OfflineSaleSyncIdempotencyHasher.ComputeHash(shopId, deviceId, firstSale);
        var concurrentSale = Sale.Create(
            shopId,
            actorId,
            OfflineSaleSyncIdempotencyHasher.ComputeKey(deviceId, firstSale.ClientSaleId),
            requestHash,
            firstSale.InvoiceNumber,
            null,
            firstSale.CustomerName,
            firstSale.CustomerPhone,
            firstSale.PaymentMethod,
            firstSale.SoldAt,
            firstSale.PaidAmount,
            firstSale.DueAmount,
            firstSale.TotalAmount,
            firstSale.TotalTaxAmount,
            [SaleItem.CreateGoods(shopId, item.Id, batch.Id, lineName: item.Name, lineCode: item.Barcode, 1m, 80m, 100m, 120m, 18m, false, false)],
            source: SaleSource.Offline,
            clientSaleId: firstSale.ClientSaleId,
            deviceId: deviceId,
            syncedAt: DateTimeOffset.UtcNow);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));
        _saleRepository.GetByClientSaleIdAsync(shopId, deviceId, firstSale.ClientSaleId, Arg.Any<CancellationToken>())
            .Returns((Sale?)null, concurrentSale);
        _saleRepository.GetByClientSaleIdAsync(shopId, deviceId, secondSale.ClientSaleId, Arg.Any<CancellationToken>())
            .Returns((Sale?)null);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new DbUpdateException("unique race")), Task.FromResult(1));
        _unitOfWork.When(x => x.ClearChanges()).Do(_ =>
        {
            batch.AddQuantity(1m, actorId);
            inventory.AddQuantity(1m, actorId);
        });
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Collection(
            result.Value.Results,
            first =>
            {
                Assert.Equal(firstSale.ClientSaleId, first.ClientSaleId);
                Assert.Equal("duplicate", first.Status);
                Assert.Equal(concurrentSale.Id, first.SaleId);
            },
            second =>
            {
                Assert.Equal(secondSale.ClientSaleId, second.ClientSaleId);
                Assert.Equal("created", second.Status);
            });
        _unitOfWork.Received(1).ClearChanges();
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenSaveFailsWithoutClientSaleRace_ReturnsFailedResultAndContinuesBatch()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id, quantity: 1m);
        var inventory = CreateInventory(shopId, item.Id, quantity: 1m);
        var firstSale = CreateSale(
            $"offline-{Guid.NewGuid():N}",
            invoiceNumber: "INV-2025-26-000001",
            items: [CreateLine(batch.Id)]);
        var secondSale = CreateSale(
            $"offline-{Guid.NewGuid():N}",
            invoiceNumber: "INV-2025-26-000002",
            items: [CreateLine(batch.Id)]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [firstSale, secondSale]);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));
        _saleRepository.GetByClientSaleIdAsync(shopId, deviceId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Sale?)null);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new DbUpdateException("fk race")), Task.FromResult(1));
        _unitOfWork.When(x => x.ClearChanges()).Do(_ =>
        {
            batch.AddQuantity(1m, actorId);
            inventory.AddQuantity(1m, actorId);
        });
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Collection(
            result.Value.Results,
            first =>
            {
                Assert.Equal(firstSale.ClientSaleId, first.ClientSaleId);
                Assert.Equal("failed", first.Status);
                Assert.Contains(first.Errors, error => error.Code == Errors.General.Unexpected().Code);
            },
            second =>
            {
                Assert.Equal(secondSale.ClientSaleId, second.ClientSaleId);
                Assert.Equal("created", second.Status);
            });
        _unitOfWork.Received(1).ClearChanges();
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenInvoiceNumberUniqueSaveFails_ReturnsInvoiceConflictResult()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", items: [CreateLine(batch.Id)]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));
        _saleRepository.GetByClientSaleIdAsync(shopId, deviceId, sale.ClientSaleId, Arg.Any<CancellationToken>())
            .Returns((Sale?)null);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(
                new DbUpdateException(
                    "unique race",
                    new InvalidOperationException("ix_sales_shop_id_invoice_number"))),
                Task.FromResult(1));
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("NeedsReview", syncResult.Status);
        Assert.Contains(syncResult.Errors, error => error.Code == Errors.Sale.InvoiceNumberAlreadyUsed.Code);
        _unitOfWork.Received(1).ClearChanges();
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ServiceLineWithoutServiceId_ReturnsFailedResult()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var serviceLine = new OfflineSaleSyncLineCommand(
            "SVC-001", "", "Repair", 1m, 0m, 500m, 0m, 18m, false, Guid.Empty,
            500m, 0m, 0m, 500m, 90m, 590m, null, null, InstantDiscountType.None, 0m, null,
            LineType: "Service",
            ServiceId: null);
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", paidAmount: 590m, items: [serviceLine]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("failed", syncResult.Status);
        Assert.Contains(syncResult.Errors, error => error.Code == Errors.Sale.ServiceIdRequired.Code);
        await _saleRepository.DidNotReceive().AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ServiceOnlyOfflineSale_CreatesSaleWithoutStockAdjustment()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var serviceId = Guid.NewGuid();
        var service = Service.Create(shopId, "SVC-001", "Repair Service", null, 500m, null, 18m, false, true, Guid.NewGuid());
        var serviceLine = new OfflineSaleSyncLineCommand(
            "SVC-001", "", "Repair Service", 1m, 0m, 500m, 0m, 18m, false, Guid.Empty,
            500m, 0m, 0m, 500m, 90m, 590m, null, null, InstantDiscountType.None, 0m, null,
            LineType: "Service",
            ServiceId: serviceId);
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", paidAmount: 590m, items: [serviceLine]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));
        _saleRepository.GetByClientSaleIdAsync(shopId, deviceId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Sale?)null);
        _saleLineValidator.ValidateLinesAsync(
                shopId,
                Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(),
                Arg.Any<List<string>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
            .Returns(callInfo =>
            {
                var commands = callInfo.ArgAt<IReadOnlyList<RecordSaleItemCommand>>(1);
                var lines = commands
                    .Select(command => new ValidatedSaleLine(command, SaleLineType.Service, null, null, null, service, false))
                    .ToList();
                return Task.FromResult<ErrorOr<SaleLineValidationResult>>(
                    new SaleLineValidationResult(lines, new Dictionary<Guid, string>()));
            });

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("created", syncResult.Status);
        await _stockTransactionRepository.DidNotReceive().AddAsync(Arg.Any<StockTransaction>(), Arg.Any<CancellationToken>());
        await _saleRepository.Received(1).AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_MixedSale_OnlyAdjustsStockForGoodsLines()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var serviceId = Guid.NewGuid();
        var service = Service.Create(shopId, "SVC-001", "Repair Service", null, 500m, null, 18m, false, true, Guid.NewGuid());
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id, quantity: 10m);
        var inventory = CreateInventory(shopId, item.Id, quantity: 10m);

        var goodsLine = CreateLine(batch.Id);
        var serviceLine = new OfflineSaleSyncLineCommand(
            "SVC-001", "", "Repair Service", 1m, 0m, 500m, 0m, 18m, false, Guid.Empty,
            500m, 0m, 0m, 500m, 90m, 590m, null, null, InstantDiscountType.None, 0m, null,
            LineType: "Service",
            ServiceId: serviceId);
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", paidAmount: 708m, items: [goodsLine, serviceLine]);
        var command = new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));
        _saleRepository.GetByClientSaleIdAsync(shopId, deviceId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Sale?)null);
        _saleLineValidator.ValidateLinesAsync(
                shopId,
                Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(),
                Arg.Any<List<string>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
            .Returns(callInfo =>
            {
                var commands = callInfo.ArgAt<IReadOnlyList<RecordSaleItemCommand>>(1);
                var lines = commands.Select<RecordSaleItemCommand, ValidatedSaleLine>(cmd =>
                    cmd.LineType == SaleLineType.Service
                        ? new ValidatedSaleLine(cmd, SaleLineType.Service, null, null, null, service, false)
                        : new ValidatedSaleLine(cmd, item, batch, inventory, false)).ToList();
                return Task.FromResult<ErrorOr<SaleLineValidationResult>>(
                    new SaleLineValidationResult(lines, new Dictionary<Guid, string> { [item.Id] = item.Name }));
            });

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("created", syncResult.Status);
        await _stockTransactionRepository.Received(1).AddAsync(Arg.Any<StockTransaction>(), Arg.Any<CancellationToken>());
        await _saleRepository.Received(1).AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenHeaderDisagreesWithLines_QuarantinesWithoutPosting()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", paidAmount: 1m, items: [CreateLine(batch.Id)]) with
        {
            SubtotalBeforeDiscount = 1m,
            TotalBeforeDiscount = 1m,
            TotalTaxAmount = 0m,
            TotalAmount = 1m,
        };
        var issues = CaptureReconciliationIssues();
        ArrangeMemberWithLease(shopId, actorId, deviceId);
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]), CancellationToken.None);

        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("NeedsReview", syncResult.Status);
        Assert.Null(syncResult.SaleId);
        Assert.Equal(Errors.Sale.OfflineArithmeticMismatch.Code, Assert.Single(syncResult.Errors).Code);
        var total = Assert.Single(syncResult.Mismatches, m => m.Path == "totalAmount");
        Assert.Equal((1m, 118m, "amount_mismatch"), (total.Supplied!.Value, total.Expected!.Value, total.Code));
        var issue = Assert.Single(issues);
        Assert.Equal(issue.Id, syncResult.ReviewCaseId);
        Assert.Equal(ReconciliationIssueType.ArithmeticConflict, issue.IssueType);
        Assert.Equal(Errors.Sale.OfflineArithmeticMismatch.Code, issue.Code);
        Assert.Null(issue.SaleId);
        Assert.Contains("totalAmount supplied 1.00 expected 118.00", issue.Message, StringComparison.Ordinal);
        await AssertNothingPostedAsync();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCalculationVersionUnknown_QuarantinesWithoutPosting()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var sale = CreateSale($"offline-{Guid.NewGuid():N}") with { CalculationVersion = 2 };
        var issues = CaptureReconciliationIssues();
        ArrangeMemberWithLease(shopId, actorId, deviceId);

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]), CancellationToken.None);

        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("NeedsReview", syncResult.Status);
        Assert.Equal(Errors.Sale.OfflineCalculationVersionUnsupported.Code, Assert.Single(syncResult.Errors).Code);
        Assert.Equal(new OfflineArithmeticMismatch("calculationVersion", 2m, 1m, "unsupported_version"), Assert.Single(syncResult.Mismatches));
        Assert.Equal(Assert.Single(issues).Id, syncResult.ReviewCaseId);
        await AssertNothingPostedAsync();
    }

    [Fact]
    public async Task HandleAsync_WhenHeldSaleIsResent_ReusesItsOpenReviewCase()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var openCaseId = Guid.NewGuid();
        var clientSaleId = $"offline-{Guid.NewGuid():N}";
        var sale = CreateSale(clientSaleId) with { CalculationVersion = 2 };
        var issues = CaptureReconciliationIssues();
        ArrangeMemberWithLease(shopId, actorId, deviceId);
        _reconciliationIssueRepository
            .FindOpenIssueIdAsync(
                shopId,
                clientSaleId,
                deviceId,
                ReconciliationIssueType.ArithmeticConflict,
                Errors.Sale.OfflineCalculationVersionUnsupported.Code,
                Arg.Any<string>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>())
            .Returns(openCaseId);

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]), CancellationToken.None);

        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("NeedsReview", syncResult.Status);
        Assert.Equal(openCaseId, syncResult.ReviewCaseId);
        Assert.Empty(issues);
        await AssertNothingPostedAsync();
    }

    [Fact]
    public async Task ChangedResendHeldForSameReason_GetsItsOwnCase()
    {
        var (shopId, actorId, deviceId) = (Guid.NewGuid(), Guid.NewGuid(), "device-1");
        var clientSaleId = $"offline-{Guid.NewGuid():N}";
        var first = CreateSale(clientSaleId) with { CalculationVersion = 2 };
        var second = first with { CalculationVersion = 3 };
        var issues = CaptureReconciliationIssues();
        ArrangeMemberWithLease(shopId, actorId, deviceId);
        StubRecordedEnvelope(candidate => candidate);
        // A case matches only when asked about its own envelope.
        _reconciliationIssueRepository
            .FindOpenIssueIdAsync(
                shopId, clientSaleId, deviceId, Arg.Any<ReconciliationIssueType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(
                issues.FirstOrDefault(i => i.OfflineSubmissionId == call.ArgAt<Guid?>(6))?.Id));

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [first, second]), CancellationToken.None);

        Assert.Equal(2, result.Value.Results.Count);
        var (r1, r2) = (result.Value.Results[0], result.Value.Results[1]);
        Assert.NotEqual(r1.ReviewCaseId, r2.ReviewCaseId);
        Assert.Equal(2, issues.Count);
        Assert.Equal(_recordedSubmissions[1].Id, issues.Single(i => i.Id == r2.ReviewCaseId).OfflineSubmissionId);
        Assert.NotEqual(_recordedSubmissions[0].Id, _recordedSubmissions[1].Id);
    }

    [Fact]
    public async Task IdenticalResendHeld_ReusesCase()
    {
        var (shopId, actorId, deviceId) = (Guid.NewGuid(), Guid.NewGuid(), "device-1");
        var clientSaleId = $"offline-{Guid.NewGuid():N}";
        var sale = CreateSale(clientSaleId) with { CalculationVersion = 2 };
        var issues = CaptureReconciliationIssues();
        ArrangeMemberWithLease(shopId, actorId, deviceId);
        OfflineSubmission? stored = null;
        // The repository answers the first stored row for an identical payload.
        StubRecordedEnvelope(candidate => stored ??= candidate);
        _reconciliationIssueRepository
            .FindOpenIssueIdAsync(
                shopId, clientSaleId, deviceId, Arg.Any<ReconciliationIssueType>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(
                issues.FirstOrDefault(i => i.OfflineSubmissionId == call.ArgAt<Guid?>(6))?.Id));

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale, sale]), CancellationToken.None);

        Assert.Equal(result.Value.Results[0].ReviewCaseId, result.Value.Results[1].ReviewCaseId);
        Assert.Single(issues);
    }

    [Fact]
    public async Task NulCharacterInText_FailsOnlyThatSale()
    {
        var (shopId, actorId, deviceId) = (Guid.NewGuid(), Guid.NewGuid(), "device-1");
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var bad = CreateSale($"offline-{Guid.NewGuid():N}", invoiceNumber: "INV-2025-26-000001", items: [CreateLine(batch.Id) with { ItemName = "Ri\0ce" }]);
        var good = CreateSale($"offline-{Guid.NewGuid():N}", invoiceNumber: "INV-2025-26-000002", items: [CreateLine(batch.Id)]);
        ArrangeMemberWithLease(shopId, actorId, deviceId);
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [bad, good]), CancellationToken.None);

        Assert.Equal("failed", result.Value.Results[0].Status);
        Assert.Equal(Errors.Sale.OfflineTextInvalid.Code, Assert.Single(result.Value.Results[0].Errors).Code);
        Assert.Equal("created", result.Value.Results[1].Status);
        Assert.DoesNotContain(_recordedSubmissions, s => s.ClientSaleId == bad.ClientSaleId);
        Assert.Contains(_recordedSubmissions, s => s.ClientSaleId == good.ClientSaleId);
    }

    [Fact]
    public async Task UnpairedSurrogateInText_FailsOnlyThatSale()
    {
        var (shopId, actorId, deviceId) = (Guid.NewGuid(), Guid.NewGuid(), "device-1");
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var loneHigh = CreateSale($"offline-{Guid.NewGuid():N}", invoiceNumber: "INV-2025-26-000001", items: [CreateLine(batch.Id) with { ItemName = "Rice \uD83D" }]);
        var loneLow = CreateSale($"offline-{Guid.NewGuid():N}", invoiceNumber: "INV-2025-26-000002", items: [CreateLine(batch.Id)]) with { CustomerName = "\uDE00 Ravi" };
        var paired = CreateSale($"offline-{Guid.NewGuid():N}", invoiceNumber: "INV-2025-26-000003", items: [CreateLine(batch.Id)]) with { CustomerName = "Ravi \U0001F600" };
        ArrangeMemberWithLease(shopId, actorId, deviceId);
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [loneHigh, loneLow, paired]), CancellationToken.None);

        Assert.Equal(Errors.Sale.OfflineTextInvalid.Code, Assert.Single(result.Value.Results[0].Errors).Code);
        Assert.Equal(Errors.Sale.OfflineTextInvalid.Code, Assert.Single(result.Value.Results[1].Errors).Code);
        Assert.Equal("created", result.Value.Results[2].Status);
        Assert.DoesNotContain(_recordedSubmissions, s => s.ClientSaleId == loneHigh.ClientSaleId || s.ClientSaleId == loneLow.ClientSaleId);
    }

    [Fact]
    public async Task StageAsync_WhenAttributedAndRecordingUsersDiffer_SplitsAuthorship()
    {
        var (shopId, attributed, recorder, deviceId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "device-1");
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id, quantity: 1m);
        var inventory = CreateInventory(shopId, item.Id, quantity: 1m);
        var customer = Customer.Create(shopId, "Due Customer", "+919876543210", null);
        var sale = CreateSale(
            $"offline-{Guid.NewGuid():N}",
            paymentMethod: PaymentMethod.Credit,
            paidAmount: 0m,
            dueAmount: 354m,
            customerId: customer.Id,
            items: [CreateLine(batch.Id, quantity: 3m)]);
        var issues = CaptureReconciliationIssues();
        var stock = new List<StockTransaction>();
        _stockTransactionRepository
            .When(r => r.AddAsync(Arg.Any<StockTransaction>(), Arg.Any<CancellationToken>()))
            .Do(call => stock.Add(call.Arg<StockTransaction>()));
        var ledger = new List<CustomerLedgerEntry>();
        _customerLedgerEntryRepository
            .When(r => r.AddAsync(Arg.Any<CustomerLedgerEntry>(), Arg.Any<CancellationToken>()))
            .Do(call => ledger.Add(call.Arg<CustomerLedgerEntry>()));
        Sale? staged = null;
        _saleRepository
            .When(r => r.AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>()))
            .Do(call => staged = call.Arg<Sale>());
        _customerResolver.ResolveAsync(shopId, customer.Id, sale.CustomerPhone, true, PaymentMethod.Credit, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Customer?>>(customer));
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);
        var poster = new OfflineSalePoster(
            _saleLineValidator, _customerResolver, _saleRepository, _customerLedgerEntryRepository, _stockTransactionRepository, _shopRepository, _varianceAnalyzer);

        var outcome = await poster.StageAsync(
            new OfflineSalePostingRequest(
                shopId, deviceId, sale.ClientSaleId, sale, "hash", attributed, recorder, Guid.NewGuid(), Now,
                new Dictionary<Guid, DiscountRule>()),
            CancellationToken.None);

        Assert.IsType<OfflineSalePostingOutcome.Staged>(outcome);
        Assert.Equal(attributed, staged!.ActorUserId);
        var tx = Assert.Single(stock);
        Assert.Equal(attributed, tx.PerformedBy);
        Assert.Equal(recorder, tx.CreatedBy);
        Assert.Equal(recorder, Assert.Single(ledger).CreatedBy);
        Assert.NotEmpty(issues);
        Assert.All(issues, i => Assert.Equal(recorder, i.CreatedBy));
    }

    [Fact]
    public async Task StageAsync_IssuesTheDocumentUnderTheLeaseFrozenProfile()
    {
        var shopId = Guid.NewGuid();
        var (sale, item, batch, inventory) = ArrangeStagedSale(shopId, "A/2526/000201");
        var frozen = FrozenProfile(TaxRegime.Regular, "Main Shop Pvt Ltd");
        var lease = InvoiceLease.Create(shopId, Guid.NewGuid(), "device-1", 2025, "A/2526/", 201, 400, 6, Now.AddDays(-1), Now.AddDays(6), sellerProfile: frozen);
        // The shop has since been renamed; the document keeps the identity the range was reserved under.
        var renamed = Shop.Create("Renamed", "Elsewhere", "Mysuru", "Karnataka", "570001", null, null, null);
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>()).Returns(renamed);
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);
        Sale? staged = null;
        _saleRepository.When(r => r.AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>())).Do(call => staged = call.Arg<Sale>());
        var issues = CaptureReconciliationIssues();

        var outcome = await Poster().StageAsync(PostingRequest(shopId, sale, lease), CancellationToken.None);

        Assert.IsType<OfflineSalePostingOutcome.Staged>(outcome);
        var document = staged!.InvoiceDocument!;
        Assert.Equal(InvoiceDocumentType.TaxInvoice, document.DocumentType);
        Assert.Equal("A/2526/000201", document.Number);
        Assert.Equal(2025, document.FiscalYearStart);
        Assert.Equal("Main Shop Pvt Ltd", document.Seller.LegalName);
        Assert.Equal([TaxComponentKind.Cgst, TaxComponentKind.Sgst], document.Totals.Components.Select(c => c.Kind));
        Assert.DoesNotContain(issues, i => i.IssueType == ReconciliationIssueType.DocumentReview);
    }

    [Fact]
    public async Task StageAsync_WhenAWeighedQuantityIsSold_PostsItAndPrintsTheKilogramUnit()
    {
        var shopId = Guid.NewGuid();
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id, quantity: 5m);
        var inventory = CreateInventory(shopId, item.Id, quantity: 5m);
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", "A/2526/000201", paidAmount: 29.5m, items: [CreateLine(batch.Id, quantity: 0.25m)]);
        var lease = InvoiceLease.Create(shopId, Guid.NewGuid(), "device-1", 2025, "A/2526/", 201, 400, 6, Now.AddDays(-1), Now.AddDays(6), sellerProfile: FrozenProfile(TaxRegime.Regular, "Main Shop Pvt Ltd"));
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);
        Sale? staged = null;
        _saleRepository.When(r => r.AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>())).Do(call => staged = call.Arg<Sale>());
        CaptureReconciliationIssues();

        var outcome = await Poster().StageAsync(PostingRequest(shopId, sale, lease), CancellationToken.None);

        Assert.IsType<OfflineSalePostingOutcome.Staged>(outcome);
        var line = Assert.Single(staged!.InvoiceDocument!.Lines);
        Assert.Equal("KGS", line.Uom);
        Assert.Equal(0.25m, line.Quantity);
        Assert.Equal(0.25m, Assert.Single(staged.Items).Quantity);
    }

    [Fact]
    public async Task StageAsync_WhenTheFrozenProfileCannotIssue_PostsWithoutADocumentAndFilesAReview()
    {
        var shopId = Guid.NewGuid();
        var (sale, item, batch, inventory) = ArrangeStagedSale(shopId, "A/2526/000201");
        var lease = InvoiceLease.Create(shopId, Guid.NewGuid(), "device-1", 2025, "A/2526/", 201, 400, 6, Now.AddDays(-1), Now.AddDays(6), sellerProfile: FrozenProfile(TaxRegime.Composition, "Main"));
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);
        Sale? staged = null;
        _saleRepository.When(r => r.AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>())).Do(call => staged = call.Arg<Sale>());
        var issues = CaptureReconciliationIssues();

        var outcome = await Poster().StageAsync(PostingRequest(shopId, sale, lease), CancellationToken.None);

        Assert.IsType<OfflineSalePostingOutcome.Staged>(outcome);
        Assert.Null(staged!.InvoiceDocument);
        var review = Assert.Single(issues, i => i.IssueType == ReconciliationIssueType.DocumentReview);
        Assert.Equal(Errors.Invoice.CompositionCannotCollectTax.Code, review.Code);
    }

    private OfflineSalePoster Poster() =>
        new(_saleLineValidator, _customerResolver, _saleRepository, _customerLedgerEntryRepository, _stockTransactionRepository, _shopRepository, _varianceAnalyzer);

    private static OfflineSalePostingRequest PostingRequest(Guid shopId, OfflineSaleSyncCommand sale, InvoiceLease lease) =>
        new(shopId, "device-1", sale.ClientSaleId, sale, "hash", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, new Dictionary<Guid, DiscountRule>(), lease);

    private static (OfflineSaleSyncCommand Sale, Item Item, InventoryBatch Batch, Domain.Entities.Inventory Inventory) ArrangeStagedSale(Guid shopId, string invoiceNumber)
    {
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id, quantity: 5m);
        var inventory = CreateInventory(shopId, item.Id, quantity: 5m);
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", invoiceNumber, items: [CreateLine(batch.Id)]);
        return (sale, item, batch, inventory);
    }

    private static InvoiceSellerProfile FrozenProfile(TaxRegime regime, string legalName)
    {
        var shop = Shop.Create("Main", "42 MG Road", "Bengaluru", "Karnataka", "560001", null, null, "29AAPFU0939F1ZR");
        Assert.False(shop.DeclareTaxProfile(legalName, "29", regime, "29AAPFU0939F1ZR", null, EInvoiceStatus.BelowThreshold, Guid.NewGuid(), Now).IsError);
        return InvoiceDocumentBuilder.CaptureSellerProfile(shop).Value;
    }

    [Fact]
    public async Task HandleAsync_WhenItemDiscountExceedsBasis_QuarantinesWithoutPosting()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var sale = CreateSale(
            $"offline-{Guid.NewGuid():N}",
            paidAmount: 0m,
            items: [CreateLine() with { ItemDiscountAmount = 150m, TaxableAmount = 0m, TaxAmount = 0m, TotalAmount = 0m }]) with { CalculationVersion = 1 };
        CaptureReconciliationIssues();
        ArrangeMemberWithLease(shopId, actorId, deviceId);

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]), CancellationToken.None);

        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("NeedsReview", syncResult.Status);
        var discount = Assert.Single(syncResult.Mismatches, m => m.Path == "items[0].itemDiscountAmount");
        Assert.Equal("discount_exceeds_basis", discount.Code);
        await AssertNothingPostedAsync();
    }

    [Fact]
    public async Task HandleAsync_WhenCatalogPriceChangedAfterIssuance_PreservesFrozenAmounts()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var deviceId = "device-1";
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id, salesPrice: 200m, mrp: 250m);
        var inventory = CreateInventory(shopId, item.Id);
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", items: [CreateLine(batch.Id)]) with { CalculationVersion = 1 };
        var issues = CaptureReconciliationIssues();
        ArrangeMemberWithLease(shopId, actorId, deviceId);
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]), CancellationToken.None);

        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("SyncedWithWarnings", syncResult.Status);
        Assert.Empty(syncResult.Mismatches);
        Assert.Contains(syncResult.Warnings, w => w.Contains("printed 100", StringComparison.Ordinal) && w.Contains("current 200", StringComparison.Ordinal));
        await _saleRepository.Received(1).AddAsync(
            Arg.Is<Sale>(s => s.TotalAmount == 118m && s.TotalTaxAmount == 18m && s.Items.Single().SalesPrice == 100m && s.Items.Single().TotalAmount == 118m),
            Arg.Any<CancellationToken>());
        Assert.Contains(issues, i => i.IssueType == ReconciliationIssueType.PricingVariance);
        Assert.DoesNotContain(issues, i => i.IssueType == ReconciliationIssueType.ArithmeticConflict);
    }

    // ---- Envelope-first sync, historical leases and issuer attribution ----

    private static readonly DateTimeOffset Day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Day(int day) => Day1.AddDays(day - 1);

    [Fact]
    public async Task ExpiredLease_UnverifiedIssueTime_RequiresReview()
    {
        var (shopId, actorId, deviceId) = (Guid.NewGuid(), Guid.NewGuid(), "device-1");
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var lease = CreateLease(shopId, deviceId, reservedAt: Day(1), expiresAt: Day(8));
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", items: [CreateLine(batch.Id)]) with { SoldAt = Day(3) };
        _clock.UtcNow = Day(10);
        var issues = CaptureReconciliationIssues();
        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>()).Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, lease);
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]), CancellationToken.None);

        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("NeedsReview", syncResult.Status);
        Assert.Null(syncResult.SaleId);
        Assert.Equal(Errors.OfflineReconciliation.HistoricalLeaseReviewRequired.Code, Assert.Single(syncResult.Errors).Code);
        var envelope = Assert.Single(_recordedSubmissions);
        Assert.Equal(Day(10), envelope.ReceivedAt);
        Assert.Equal(Day(3), envelope.ClaimedSoldAt);
        Assert.Equal(lease.Id, envelope.OriginalLeaseId);
        Assert.Equal(sale.InvoiceNumber, envelope.InvoiceNumber);
        Assert.Equal(OfflineSubmissionSource.Sync, envelope.Source);
        Assert.Equal(actorId, envelope.SubmittedByUserId);
        Assert.Null(envelope.SaleId);
        var issue = Assert.Single(issues);
        Assert.Equal(issue.Id, syncResult.ReviewCaseId);
        Assert.Equal(ReconciliationIssueType.IssueTimingReview, issue.IssueType);
        Assert.Equal(Errors.OfflineReconciliation.HistoricalLeaseReviewRequired.Code, issue.Code);
        Assert.Equal(sale.InvoiceNumber, issue.InvoiceNumber);
        Assert.Equal(envelope.Id, issue.OfflineSubmissionId);
        Assert.Null(issue.SaleId);
        await AssertNothingPostedAsync();
    }

    [Fact]
    public async Task TrustedPriorReceipt_AllowsIdenticalReplay()
    {
        var (shopId, actorId, deviceId) = (Guid.NewGuid(), Guid.NewGuid(), "device-1");
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", items: [CreateLine(batch.Id)]) with { SoldAt = Day(3) };
        _clock.UtcNow = Day(10);
        OfflineSubmission? stored = null;
        StubRecordedEnvelope(candidate => stored = FirstReceipt(candidate, Day(5)));
        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>()).Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId, reservedAt: Day(1), expiresAt: Day(8)));
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]), CancellationToken.None);

        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("created", syncResult.Status);
        await _saleRepository.Received(1).AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        Assert.NotNull(stored);
        Assert.Equal(syncResult.SaleId, stored.SaleId);
    }

    [Fact]
    public async Task BackdatedAfterExpiry_NotAutoAccepted()
    {
        var result = await SyncAgainstHistoricalLeaseAsync(claimedSoldAt: Day(8).AddMinutes(-1), now: Day(8).AddMinutes(1));

        Assert.Equal("NeedsReview", result.Status);
        Assert.Equal(Errors.OfflineReconciliation.HistoricalLeaseReviewRequired.Code, Assert.Single(result.Errors).Code);
        await AssertNothingPostedAsync();
    }

    [Fact]
    public async Task IssuedAfterExpiry_IsReviewed()
    {
        var issues = CaptureReconciliationIssues();

        var result = await SyncAgainstHistoricalLeaseAsync(claimedSoldAt: Day(9), now: Day(10));

        Assert.Equal("NeedsReview", result.Status);
        Assert.Equal(Errors.OfflineReconciliation.IssuedAfterLeaseExpiry.Code, Assert.Single(result.Errors).Code);
        Assert.Equal(ReconciliationIssueType.IssueTimingReview, Assert.Single(issues).IssueType);
        await AssertNothingPostedAsync();
    }

    [Theory]
    [InlineData(6, "OfflineReconciliation.IssueTimeInFuture")]
    [InlineData(-(4 * 24 * 60) - 60, "OfflineReconciliation.IssueTimeBeforeLease")]
    public async Task FutureOrSkewedIssueTime_IsReviewed(int claimedMinutesFromNow, string expectedCode)
    {
        var issues = CaptureReconciliationIssues();

        var result = await SyncAgainstHistoricalLeaseAsync(claimedSoldAt: Day(5).AddMinutes(claimedMinutesFromNow), now: Day(5));

        Assert.Equal("NeedsReview", result.Status);
        Assert.Equal(expectedCode, Assert.Single(result.Errors).Code);
        var issue = Assert.Single(issues);
        Assert.Equal(ReconciliationIssueType.IssueTimingReview, issue.IssueType);
        Assert.Equal(expectedCode, issue.Code);
        await AssertNothingPostedAsync();
    }

    [Fact]
    public async Task IssueTimeAtSkewAllowance_Posts()
    {
        var result = await SyncAgainstHistoricalLeaseAsync(claimedSoldAt: Day(5).AddMinutes(5), now: Day(5));

        Assert.Equal("created", result.Status);
        await _saleRepository.Received(1).AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConflictingEnvelope_Retained()
    {
        var (shopId, actorId, deviceId) = (Guid.NewGuid(), Guid.NewGuid(), "device-1");
        var original = CreateSale($"offline-{Guid.NewGuid():N}", items: [CreateLine()]);
        var originalHash = OfflineSaleSyncIdempotencyHasher.ComputeHash(shopId, deviceId, original);
        var existingSale = Sale.Create(
            shopId,
            actorId,
            OfflineSaleSyncIdempotencyHasher.ComputeKey(deviceId, original.ClientSaleId),
            originalHash,
            original.InvoiceNumber,
            null,
            original.CustomerName,
            original.CustomerPhone,
            original.PaymentMethod,
            original.SoldAt,
            original.PaidAmount,
            original.DueAmount,
            original.TotalAmount,
            original.TotalTaxAmount,
            [SaleItem.CreateGoods(shopId, Guid.NewGuid(), Guid.NewGuid(), lineName: "Item", lineCode: "BC-001", 1m, 80m, 100m, 120m, 18m, false, false)],
            source: SaleSource.Offline,
            clientSaleId: original.ClientSaleId,
            deviceId: deviceId,
            syncedAt: Now.AddDays(-1));
        var resend = original with { PaidAmount = 236m, TotalAmount = 236m };
        var resendHash = OfflineSaleSyncIdempotencyHasher.ComputeHash(shopId, deviceId, resend);
        var issues = CaptureReconciliationIssues();
        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>()).Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));
        _saleRepository.GetByClientSaleIdAsync(shopId, deviceId, original.ClientSaleId, Arg.Any<CancellationToken>()).Returns(existingSale);

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [resend]), CancellationToken.None);

        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("NeedsReview", syncResult.Status);
        Assert.Equal(Errors.Sale.IdempotencyConflict.Code, Assert.Single(syncResult.Errors).Code);
        Assert.NotEqual(originalHash, resendHash);
        await _offlineSubmissionRepository.Received(1).RecordAsync(
            Arg.Is<OfflineSubmission>(s => s.PayloadHash == resendHash && s.ClientSaleId == original.ClientSaleId),
            Arg.Any<CancellationToken>());
        var envelope = Assert.Single(_recordedSubmissions);
        Assert.Null(envelope.SaleId);
        var replayed = OfflineSubmissionPayload.Deserialize(envelope.PayloadJson);
        Assert.NotNull(replayed);
        Assert.Equal(resendHash, OfflineSaleSyncIdempotencyHasher.ComputeHash(shopId, deviceId, replayed));
        var issue = Assert.Single(issues);
        Assert.Equal(issue.Id, syncResult.ReviewCaseId);
        Assert.Equal(ReconciliationIssueType.ValidationConflict, issue.IssueType);
        Assert.Equal(Errors.Sale.IdempotencyConflict.Code, issue.Code);
        Assert.Equal(existingSale.Id, issue.SaleId);
        Assert.Equal(envelope.Id, issue.OfflineSubmissionId);
        Assert.Equal(original.InvoiceNumber, issue.InvoiceNumber);
        Assert.Equal(118m, existingSale.TotalAmount);
        Assert.Equal(originalHash, existingSale.RequestHash);
        await AssertNothingPostedAsync();
    }

    [Fact]
    public async Task DuplicateOfSaleWithoutEnvelope_LinksTheResentEnvelopeOnce()
    {
        var (shopId, actorId, deviceId) = (Guid.NewGuid(), Guid.NewGuid(), "device-1");
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", items: [CreateLine()]);
        var existingSale = Sale.Create(
            shopId,
            actorId,
            OfflineSaleSyncIdempotencyHasher.ComputeKey(deviceId, sale.ClientSaleId),
            OfflineSaleSyncIdempotencyHasher.ComputeHash(shopId, deviceId, sale),
            sale.InvoiceNumber,
            null,
            sale.CustomerName,
            sale.CustomerPhone,
            sale.PaymentMethod,
            sale.SoldAt,
            sale.PaidAmount,
            sale.DueAmount,
            sale.TotalAmount,
            sale.TotalTaxAmount,
            [SaleItem.CreateGoods(shopId, Guid.NewGuid(), Guid.NewGuid(), lineName: "Item", lineCode: "BC-001", 1m, 80m, 100m, 120m, 18m, false, false)],
            source: SaleSource.Offline,
            clientSaleId: sale.ClientSaleId,
            deviceId: deviceId,
            syncedAt: Now.AddDays(-1));
        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>()).Returns(CreateMemberUser(shopId));
        _saleRepository.GetByClientSaleIdAsync(shopId, deviceId, sale.ClientSaleId, Arg.Any<CancellationToken>()).Returns(existingSale);

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]), CancellationToken.None);

        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("duplicate", syncResult.Status);
        Assert.Equal(existingSale.Id, Assert.Single(_recordedSubmissions).SaleId);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await AssertNothingPostedAsync();
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task IssuerDiffersFromSubmitter_HeldForProvenance(bool issuerWasMember, bool issuerHasDeviceHistory, bool issuerVerified)
    {
        var (shopId, actorId, deviceId) = (Guid.NewGuid(), Guid.NewGuid(), "device-1");
        var issuerId = Guid.NewGuid();
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", items: [CreateLine(batch.Id)]) with { IssuedByUserId = issuerId };
        var issues = CaptureReconciliationIssues();
        ArrangeMemberWithLease(shopId, actorId, deviceId);
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);
        var removedMembership = ShopMembership.Create(shopId, issuerId, ShopRole.Staff, false);
        removedMembership.ChangeAuthority(ShopRole.Staff, MembershipStatus.Removed);
        _shopRepository.GetMembershipsForUsersInShopsAsync(
                Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { issuerId })),
                Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { shopId })),
                Arg.Any<CancellationToken>())
            .Returns(issuerWasMember ? [removedMembership] : []);
        _offlineSubmissionRepository.HasDeviceHistoryAsync(shopId, deviceId, issuerId, Arg.Any<CancellationToken>())
            .Returns(issuerHasDeviceHistory);

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]), CancellationToken.None);

        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("NeedsReview", syncResult.Status);
        Assert.Equal(Errors.OfflineReconciliation.IssuerNotSubmitter.Code, Assert.Single(syncResult.Errors).Code);
        var envelope = Assert.Single(_recordedSubmissions);
        Assert.Equal(actorId, envelope.SubmittedByUserId);
        Assert.Equal(issuerId, envelope.AssertedIssuerUserId);
        Assert.Equal(issuerVerified ? issuerId : null, envelope.OriginalIssuerUserId);
        Assert.Equal(issuerVerified ? issuerId : null, envelope.AttributableUserId);
        var issue = Assert.Single(issues);
        Assert.Equal(ReconciliationIssueType.ProvenanceReview, issue.IssueType);
        Assert.Equal(Errors.OfflineReconciliation.IssuerNotSubmitter.Code, issue.Code);
        Assert.Equal(envelope.Id, issue.OfflineSubmissionId);
        Assert.Equal(sale.InvoiceNumber, issue.InvoiceNumber);
        Assert.Equal(actorId, issue.CreatedBy);
        await AssertNothingPostedAsync();
    }

    [Fact]
    public async Task IssuerEqualsSubmitter_PostsWithVerifiedIssuer()
    {
        var (shopId, actorId, deviceId) = (Guid.NewGuid(), Guid.NewGuid(), "device-1");
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", items: [CreateLine(batch.Id)]) with { IssuedByUserId = actorId };
        ArrangeMemberWithLease(shopId, actorId, deviceId);
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]), CancellationToken.None);

        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("created", syncResult.Status);
        var envelope = Assert.Single(_recordedSubmissions);
        Assert.Equal(actorId, envelope.AssertedIssuerUserId);
        Assert.Equal(actorId, envelope.OriginalIssuerUserId);
        Assert.Equal(syncResult.SaleId, envelope.SaleId);
        Assert.Equal(OfflineSaleSyncIdempotencyHasher.ComputeHash(shopId, deviceId, sale), envelope.PayloadHash);
        await _saleRepository.Received(1).AddAsync(Arg.Is<Sale>(s => s.ActorUserId == actorId), Arg.Any<CancellationToken>());
        await _shopRepository.DidNotReceive().GetMembershipsForUsersInShopsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
        await _offlineSubmissionRepository.DidNotReceive().HasDeviceHistoryAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LegacyPayloadWithoutIssuer_PostsAsBefore()
    {
        var (shopId, actorId, deviceId) = (Guid.NewGuid(), Guid.NewGuid(), "device-1");
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", items: [CreateLine(batch.Id)]);
        ArrangeMemberWithLease(shopId, actorId, deviceId);
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]), CancellationToken.None);

        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("created", syncResult.Status);
        var envelope = Assert.Single(_recordedSubmissions);
        Assert.Null(envelope.AssertedIssuerUserId);
        Assert.Null(envelope.OriginalIssuerUserId);
        Assert.Equal(actorId, envelope.AttributableUserId);
        Assert.Equal(syncResult.SaleId, envelope.SaleId);
        Assert.Equal(Now, envelope.ReceivedAt);
        await _saleRepository.Received(1).AddAsync(Arg.Is<Sale>(s => s.ActorUserId == actorId && s.SyncedAt == Now), Arg.Any<CancellationToken>());
        await _stockTransactionRepository.Received(1).AddAsync(
            Arg.Is<StockTransaction>(tx => tx.PerformedBy == actorId && tx.CreatedBy == actorId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnvelopeRecordedBeforeStaging()
    {
        var (shopId, actorId, deviceId) = (Guid.NewGuid(), Guid.NewGuid(), "device-1");
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", items: [CreateLine(batch.Id)]);
        ArrangeMemberWithLease(shopId, actorId, deviceId);
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]), CancellationToken.None);

        Assert.Equal("created", Assert.Single(result.Value.Results).Status);
        Received.InOrder(() =>
        {
            _offlineSubmissionRepository.RecordAsync(Arg.Any<OfflineSubmission>(), Arg.Any<CancellationToken>());
            _saleRepository.GetByClientSaleIdAsync(shopId, deviceId, sale.ClientSaleId, Arg.Any<CancellationToken>());
            _saleRepository.AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task StagedVarianceIssues_CarryInvoiceNumberAndEnvelope()
    {
        var (shopId, actorId, deviceId) = (Guid.NewGuid(), Guid.NewGuid(), "device-1");
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id, quantity: 1m);
        var inventory = CreateInventory(shopId, item.Id, quantity: 1m);
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", paidAmount: 354m, items: [CreateLine(batch.Id, quantity: 3m)]);
        var issues = CaptureReconciliationIssues();
        ArrangeMemberWithLease(shopId, actorId, deviceId);
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]), CancellationToken.None);

        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("SyncedWithWarnings", syncResult.Status);
        var envelope = Assert.Single(_recordedSubmissions);
        var stock = Assert.Single(issues, i => i.IssueType == ReconciliationIssueType.StockVariance);
        Assert.Equal(sale.InvoiceNumber, stock.InvoiceNumber);
        Assert.Equal(envelope.Id, stock.OfflineSubmissionId);
        Assert.Equal(syncResult.SaleId, stock.SaleId);
        Assert.Equal(actorId, stock.CreatedBy);
    }

    private async Task<OfflineSaleSyncResultDto> SyncAgainstHistoricalLeaseAsync(DateTimeOffset claimedSoldAt, DateTimeOffset now)
    {
        var (shopId, actorId, deviceId) = (Guid.NewGuid(), Guid.NewGuid(), "device-1");
        var item = CreateItem(shopId);
        var batch = CreateBatch(shopId, item.Id);
        var inventory = CreateInventory(shopId, item.Id);
        var sale = CreateSale($"offline-{Guid.NewGuid():N}", items: [CreateLine(batch.Id)]) with { SoldAt = claimedSoldAt };
        _clock.UtcNow = now;
        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>()).Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId, reservedAt: Day(1), expiresAt: Day(8)));
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(new SyncOfflineSalesCommand(actorId, shopId, deviceId, [sale]), CancellationToken.None);

        Assert.False(result.IsError);
        return Assert.Single(result.Value.Results);
    }

    private void StubLeases(Guid shopId, string deviceId, params InvoiceLease[] leases) =>
        _invoiceLeaseRepository.FindReservedRangeAsync(shopId, deviceId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(MatchLease(leases, call.ArgAt<string>(2))));

    // Mirrors the repository: the lease whose prefix and range contain the number, expiry ignored.
    private static InvoiceLease? MatchLease(IEnumerable<InvoiceLease> leases, string invoiceNumber) =>
        leases.FirstOrDefault(lease =>
            invoiceNumber.StartsWith(lease.Prefix, StringComparison.Ordinal)
            && int.TryParse(invoiceNumber[lease.Prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number >= lease.RangeStart
            && number <= lease.RangeEnd);

    /// <summary>The repository stores the candidate and answers the stored (first) row.</summary>
    private void StubRecordedEnvelope(Func<OfflineSubmission, OfflineSubmission> storedRow) =>
        _offlineSubmissionRepository.RecordAsync(Arg.Any<OfflineSubmission>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var candidate = call.Arg<OfflineSubmission>();
                _recordedSubmissions.Add(candidate);
                return Task.FromResult(storedRow(candidate));
            });

    private static OfflineSubmission FirstReceipt(OfflineSubmission candidate, DateTimeOffset receivedAt) =>
        OfflineSubmission.Record(
            candidate.ShopId,
            candidate.DeviceId,
            candidate.ClientSaleId,
            candidate.InvoiceNumber,
            candidate.PayloadHash,
            candidate.PayloadJson,
            candidate.ClaimedSoldAt,
            receivedAt,
            candidate.Source,
            candidate.SubmittedByUserId,
            candidate.AssertedIssuerUserId,
            candidate.OriginalIssuerUserId,
            candidate.OriginalLeaseId,
            candidate.RecoveryOperationId);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private List<ReconciliationIssue> CaptureReconciliationIssues()
    {
        var issues = new List<ReconciliationIssue>();
        _reconciliationIssueRepository
            .When(r => r.AddAsync(Arg.Any<ReconciliationIssue>(), Arg.Any<CancellationToken>()))
            .Do(call => issues.Add(call.Arg<ReconciliationIssue>()));
        return issues;
    }

    private void ArrangeMemberWithLease(Guid shopId, Guid actorId, string deviceId)
    {
        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        StubLeases(shopId, deviceId, CreateLease(shopId, deviceId));
    }

    private async Task AssertNothingPostedAsync()
    {
        await _saleLineValidator.DidNotReceive()
            .ValidateLinesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>());
        await _saleRepository.DidNotReceive().AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        await _stockTransactionRepository.DidNotReceive().AddAsync(Arg.Any<StockTransaction>(), Arg.Any<CancellationToken>());
        await _customerLedgerEntryRepository.DidNotReceive().AddAsync(Arg.Any<CustomerLedgerEntry>(), Arg.Any<CancellationToken>());
    }
}
