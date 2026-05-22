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
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public SyncOfflineSalesCommandHandlerTests()
    {
        _customerResolver.ResolveAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid?>(),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                Arg.Any<PaymentMethod>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Customer?>>((Customer?)null));
    }

    private SyncOfflineSalesCommandHandler CreateHandler() =>
        new(
            _userRepository,
            _invoiceLeaseRepository,
            _saleLineValidator,
            _customerResolver,
            _saleRepository,
            _customerLedgerEntryRepository,
            _stockTransactionRepository,
            _unitOfWork);

    private static User CreateMemberUser(Guid shopId)
    {
        var user = User.CreateWithEmail("user@test.com", "hash", "Test", "User");
        user.AddShopMembership(ShopMembership.Create(shopId, user.Id, ShopRole.Owner, true));
        return user;
    }

    private static InvoiceLease CreateLease(Guid shopId, string deviceId = "device-1") =>
        InvoiceLease.Create(
            shopId,
            Guid.NewGuid(),
            deviceId,
            2025,
            "INV-2025-26-",
            1,
            50,
            6,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(1));

    private static Item CreateItem(Guid shopId, string barcode = "BC-001", string name = "Rice") =>
        Item.Create(shopId, name, "desc", "kg", barcode, true, Guid.NewGuid());

    private static InventoryBatch CreateBatch(
        Guid shopId,
        Guid itemId,
        string batchNumber = "B-01",
        decimal quantity = 100m)
    {
        return InventoryBatch.Create(
            shopId,
            itemId,
            batchNumber,
            quantity,
            costPrice: 80m,
            mrp: 120m,
            salesPrice: 100m,
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

        return new OfflineSaleSyncCommand(
            clientSaleId,
            invoiceNumber,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            customerId,
            "Ravi",
            customerPhone,
            paymentMethod,
            paidAmount,
            dueAmount,
            subtotal,
            total,
            0m,
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
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var commands = callInfo.ArgAt<IReadOnlyList<RecordSaleItemCommand>>(1);
                var lines = commands
                    .Select(command => new ValidatedSaleLine(command, item, batch, inventory, false))
                    .ToList();

                return Task.FromResult<ErrorOr<SaleLineValidationResult>>(
                    new SaleLineValidationResult(lines, new Dictionary<Guid, string> { [item.Id] = item.Name }));
            });
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

        var saleItem = SaleItem.Create(
            shopId,
            Guid.NewGuid(),
            line.InventoryBatchId,
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
            syncedAt: DateTimeOffset.UtcNow);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(user);
        _invoiceLeaseRepository.GetActiveByDeviceAsync(shopId, deviceId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _saleRepository.GetByClientSaleIdAsync(shopId, deviceId, sale.ClientSaleId, Arg.Any<CancellationToken>())
            .Returns(existingSale);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var item = Assert.Single(result.Value.Results);
        Assert.Equal("duplicate", item.Status);
        Assert.Equal(existingSale.Id, item.SaleId);
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
        _invoiceLeaseRepository.GetActiveByDeviceAsync(shopId, deviceId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([CreateLease(shopId, deviceId)]);
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
        _invoiceLeaseRepository.GetActiveByDeviceAsync(shopId, deviceId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([CreateLease(shopId, deviceId)]);
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
        _invoiceLeaseRepository.GetActiveByDeviceAsync(shopId, deviceId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([CreateLease(shopId, deviceId)]);
        _customerResolver.ResolveAsync(shopId, customer.Id, sale.CustomerPhone, true, PaymentMethod.Credit, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Customer?>>(customer));
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("created", Assert.Single(result.Value.Results).Status);
        await _customerLedgerEntryRepository.Received(1).AddAsync(
            Arg.Is<CustomerLedgerEntry>(entry =>
                entry.CustomerId == customer.Id
                && entry.EntryType == CustomerLedgerEntryType.SaleDue
                && entry.Amount == 118m),
            Arg.Any<CancellationToken>());
        await _saleRepository.Received(1).AddAsync(
            Arg.Is<Sale>(s => s.CustomerId == customer.Id && s.CustomerName == customer.Name),
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
            [SaleItem.Create(shopId, item.Id, batch.Id, 1m, 80m, 100m, 120m, 18m, false, false)],
            source: SaleSource.Offline,
            clientSaleId: sale.ClientSaleId,
            deviceId: deviceId,
            syncedAt: DateTimeOffset.UtcNow);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        _invoiceLeaseRepository.GetActiveByDeviceAsync(shopId, deviceId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([CreateLease(shopId, deviceId)]);
        _saleRepository.GetByClientSaleIdAsync(shopId, deviceId, sale.ClientSaleId, Arg.Any<CancellationToken>())
            .Returns((Sale?)null, concurrentSale);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new DbUpdateException("unique race")));
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("duplicate", syncResult.Status);
        Assert.Equal(concurrentSale.Id, syncResult.SaleId);
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
            [SaleItem.Create(shopId, item.Id, batch.Id, 1m, 80m, 100m, 120m, 18m, false, false)],
            source: SaleSource.Offline,
            clientSaleId: sale.ClientSaleId,
            deviceId: deviceId,
            syncedAt: DateTimeOffset.UtcNow);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        _invoiceLeaseRepository.GetActiveByDeviceAsync(shopId, deviceId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([CreateLease(shopId, deviceId)]);
        _saleRepository.GetByClientSaleIdAsync(shopId, deviceId, sale.ClientSaleId, Arg.Any<CancellationToken>())
            .Returns((Sale?)null, concurrentSale);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new DbUpdateException("unique race")));
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("failed", syncResult.Status);
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
            [SaleItem.Create(shopId, item.Id, batch.Id, 1m, 80m, 100m, 120m, 18m, false, false)],
            source: SaleSource.Offline,
            clientSaleId: firstSale.ClientSaleId,
            deviceId: deviceId,
            syncedAt: DateTimeOffset.UtcNow);

        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(CreateMemberUser(shopId));
        _invoiceLeaseRepository.GetActiveByDeviceAsync(shopId, deviceId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([CreateLease(shopId, deviceId)]);
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
        _invoiceLeaseRepository.GetActiveByDeviceAsync(shopId, deviceId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([CreateLease(shopId, deviceId)]);
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
        _invoiceLeaseRepository.GetActiveByDeviceAsync(shopId, deviceId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([CreateLease(shopId, deviceId)]);
        _saleRepository.GetByClientSaleIdAsync(shopId, deviceId, sale.ClientSaleId, Arg.Any<CancellationToken>())
            .Returns((Sale?)null);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(
                new DbUpdateException(
                    "unique race",
                    new InvalidOperationException("ix_sales_shop_id_invoice_number"))));
        SetupSuccessfulLineValidation(_saleLineValidator, shopId, item, batch, inventory);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        var syncResult = Assert.Single(result.Value.Results);
        Assert.Equal("failed", syncResult.Status);
        Assert.Contains(syncResult.Errors, error => error.Code == Errors.Sale.InvoiceNumberAlreadyUsed.Code);
        _unitOfWork.Received(1).ClearChanges();
    }

}
