using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Application.Features.Sales.Services.Pricing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Commands.RecordSale;

public class RecordSaleCommandHandlerTests
{
    private readonly ISaleLineValidator _saleLineValidator = Substitute.For<ISaleLineValidator>();
    private readonly ISalePricingCalculator _salePricingCalculator = Substitute.For<ISalePricingCalculator>();
    private readonly ICustomerResolver _customerResolver = Substitute.For<ICustomerResolver>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly SaleDtoBuilder _saleDtoBuilder;
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();
    private readonly ICustomerLedgerEntryRepository _customerLedgerEntryRepository = Substitute.For<ICustomerLedgerEntryRepository>();
    private readonly IStockTransactionRepository _stockTransactionRepository = Substitute.For<IStockTransactionRepository>();
    private readonly ICreditNoteRedemptionRepository _creditNoteRedemptionRepository = Substitute.For<ICreditNoteRedemptionRepository>();
    private readonly ICreditNoteRepository _creditNoteRepository = Substitute.For<ICreditNoteRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public RecordSaleCommandHandlerTests()
    {
        _saleDtoBuilder = new SaleDtoBuilder(_itemRepository);
        _customerResolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Customer?>>((Customer?)null));
        _salePricingCalculator.CalculateAsync(Arg.Any<SalePricingCalculationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var request = ci.ArgAt<SalePricingCalculationRequest>(0);
                var lines = request.Lines.Select(line =>
                {
                    var preTax = decimal.Round(line.Quantity * line.SalesPrice, 2, MidpointRounding.AwayFromZero);
                    var tax = decimal.Round(preTax * line.TaxRatePercent / 100m, 2, MidpointRounding.AwayFromZero);
                    var total = decimal.Round(preTax + tax, 2, MidpointRounding.AwayFromZero);
                    return new SalePricingLineCalculation(
                        line.InventoryBatchId, line.Quantity, line.CostPrice, line.SalesPrice, line.TaxRatePercent, line.IsPriceIncludingTax,
                        preTax, 0m, 0m, preTax, tax, total, 0m, 0m, null, null);
                }).ToList();
                return Task.FromResult<ErrorOr<SalePricingCalculationResult>>(new SalePricingCalculationResult(
                    lines,
                    lines.Sum(x => x.TaxableAmount),
                    lines.Sum(x => x.TaxableAmount),
                    lines.Sum(x => x.TaxAmount),
                    0m,
                    lines.Sum(x => x.TotalAmount),
                    null,
                    []));
            });
    }

    private RecordSaleCommandHandler CreateHandler() =>
        new(_saleLineValidator, _salePricingCalculator, _customerResolver, _saleRepository, _saleDtoBuilder, _customerLedgerEntryRepository, _stockTransactionRepository, _creditNoteRedemptionRepository, _creditNoteRepository, _unitOfWork);

    private static Item MakeItem(Guid shopId, string barcode, string name = "Rice") =>
        Item.Create(shopId, name, "desc", "kg", barcode, true, Guid.NewGuid());

    private static InventoryBatch MakeBatch(
        Guid shopId,
        Guid itemId,
        string batchNumber,
        decimal quantity = 100m,
        decimal costPrice = 80m,
        bool purchaseTaxIncluded = false)
    {
        var result = InventoryBatch.Create(shopId, itemId, batchNumber,
            quantity, costPrice: costPrice, mrp: 120m, salesPrice: 100m,
            taxRatePercent: 18m, taxIncluded: false, purchaseTaxIncluded: purchaseTaxIncluded, expiryDate: null,
            manufacturingDate: null, supplierId: null, createdBy: Guid.NewGuid());
        return result.Value;
    }

    private static Domain.Entities.Inventory MakeInventory(Guid shopId, Guid itemId, decimal quantity = 100m)
    {
        var result = Domain.Entities.Inventory.Create(shopId, itemId, quantity, 10m, 500m, Guid.NewGuid());
        return result.Value;
    }

    private static RecordSaleCommand MakeCommand(
        Guid shopId, Guid actorId,
        string barcode = "BC-001", string batchNumber = "B-01",
        decimal quantity = 5m,
        Guid? inventoryBatchId = null,
        string? idempotencyKey = null,
        string? hsnCode = null,
        decimal creditNoteAppliedAmount = 0m,
        IReadOnlyList<CreditNoteRedemptionCommand>? creditNoteRedemptions = null) =>
        new(actorId, shopId, idempotencyKey ?? $"sale-{Guid.NewGuid():N}", null, "Ravi Kumar", "+919876543210",
            PaymentMethod.Cash, quantity * 118m, 0m,
            [new RecordSaleItemCommand(barcode, batchNumber, "Rice", quantity, 80m, 100m, 120m, 18m, false, inventoryBatchId ?? Guid.NewGuid(), HsnCode: hsnCode)],
            CreditNoteAppliedAmount: creditNoteAppliedAmount,
            CreditNoteRedemptions: creditNoteRedemptions ?? []);

    private static SalePricingCalculationResult BuildPricingResult(params ValidatedSaleLine[] lines)
    {
        var calculatedLines = lines.Select(line =>
            new SalePricingLineCalculation(
                line.Batch!.Id,
                line.Command.Quantity,
                line.Batch!.CostPrice,
                line.Batch!.SalesPrice,
                line.Batch!.TaxRatePercent,
                line.Batch!.TaxIncluded,
                decimal.Round(line.Command.Quantity * line.Batch!.SalesPrice, 2, MidpointRounding.AwayFromZero),
                0m,
                0m,
                decimal.Round(line.Command.Quantity * line.Batch!.SalesPrice, 2, MidpointRounding.AwayFromZero),
                decimal.Round(line.Command.Quantity * line.Batch!.SalesPrice * line.Batch!.TaxRatePercent / 100m, 2, MidpointRounding.AwayFromZero),
                decimal.Round(line.Command.Quantity * line.Batch!.SalesPrice * (1 + line.Batch!.TaxRatePercent / 100m), 2, MidpointRounding.AwayFromZero),
                0m,
                0m,
                null,
                null)).ToList();

        return new SalePricingCalculationResult(
            calculatedLines,
            calculatedLines.Sum(x => x.TaxableAmount),
            calculatedLines.Sum(x => x.TaxableAmount),
            calculatedLines.Sum(x => x.TaxAmount),
            0m,
            calculatedLines.Sum(x => x.TotalAmount),
            null,
            []);
    }

    [Fact]
    public async Task HandleAsync_WhenValid_CreatesSaleAndDeductsStock()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01", 100m);
        var inventory = MakeInventory(shopId, item.Id, 100m);

        var command = MakeCommand(shopId, actorId);
        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);
        var itemNameById = new Dictionary<Guid, string> { { item.Id, item.Name } };
        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult(new List<ValidatedSaleLine> { line }, itemNameById));

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.StartsWith("INV-", result.Value.InvoiceNumber);
        Assert.Single(result.Value.Items);
        Assert.Equal("Rice", result.Value.Items[0].ItemName);
        Assert.Equal(95m, batch.Quantity);
        Assert.Equal(95m, inventory.Quantity);
        await _saleRepository.Received(1).AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        await _stockTransactionRepository.Received(1).AddAsync(Arg.Any<StockTransaction>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenPurchaseTaxIncluded_UsesNetCostForPricingAndSaleItem()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01", costPrice: 118m, purchaseTaxIncluded: true);
        var inventory = MakeInventory(shopId, item.Id, 100m);
        var command = MakeCommand(shopId, actorId);
        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);
        var itemNameById = new Dictionary<Guid, string> { { item.Id, item.Name } };
        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult(new List<ValidatedSaleLine> { line }, itemNameById));

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        await _salePricingCalculator.Received(1).CalculateAsync(
            Arg.Is<SalePricingCalculationRequest>(request =>
                request.Lines.Count == 1
                && request.Lines[0].CostPrice == 100m),
            Arg.Any<CancellationToken>());
        await _saleRepository.Received(1).AddAsync(
            Arg.Is<Sale>(sale => sale.Items.Single().CostPrice == 100m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenSingleCreditNoteRedemption_ReducesBalanceAndPersistsRedemption()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);
        var creditNote = CreditNote.Issue(shopId, Guid.NewGuid(), 500m, "Return", "CN-001", null).Value;

        var command = MakeCommand(shopId, actorId, inventoryBatchId: batch.Id) with
        {
            PaidAmount = 490m,
            CreditNoteAppliedAmount = 100m,
            CreditNoteRedemptions = [new CreditNoteRedemptionCommand(creditNote.Code, 100m)],
        };
        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);
        var itemNameById = new Dictionary<Guid, string> { { item.Id, item.Name } };
        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult(new List<ValidatedSaleLine> { line }, itemNameById));
        _creditNoteRepository.GetByCodeAsync(shopId, creditNote.Code, Arg.Any<CancellationToken>())
            .Returns(creditNote);

        Sale? capturedSale = null;
        await _saleRepository.AddAsync(Arg.Do<Sale>(sale => capturedSale = sale), Arg.Any<CancellationToken>());

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(capturedSale);
        Assert.Equal(100m, capturedSale!.CreditNoteAppliedAmount);
        Assert.Equal(400m, creditNote.AvailableBalance);
        Assert.Single(creditNote.Redemptions);
        await _creditNoteRedemptionRepository.Received(1).AddAsync(Arg.Any<CreditNoteRedemption>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCreditNoteAppliedAmountHasNoRedemption_ReturnsValidationError()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);

        var command = MakeCommand(shopId, actorId, inventoryBatchId: batch.Id) with
        {
            PaidAmount = 68m,
            CreditNoteAppliedAmount = 50m,
        };
        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);
        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult([line], new Dictionary<Guid, string> { { item.Id, item.Name } }));

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.CreditNoteRedemptionsSplitMismatch.Code, result.FirstError.Code);
        await _saleRepository.DidNotReceive().AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        await _creditNoteRepository.DidNotReceive().GetByCodeAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _creditNoteRedemptionRepository.DidNotReceive().AddAsync(Arg.Any<CreditNoteRedemption>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenMultipleCreditNoteRedemptions_ReturnsValidationError()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);
        var command = MakeCommand(shopId, actorId, inventoryBatchId: batch.Id) with
        {
            CreditNoteAppliedAmount = 100m,
            CreditNoteRedemptions =
            [
                new CreditNoteRedemptionCommand("CN-001", 50m),
                new CreditNoteRedemptionCommand("CN-002", 50m),
            ],
        };

        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);
        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult([line], new Dictionary<Guid, string> { { item.Id, item.Name } }));

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.CreditNoteRedemptionsLimitExceeded.Code, result.FirstError.Code);
        await _saleRepository.DidNotReceive().AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCreditNoteBalanceInsufficient_ReturnsError()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);
        var creditNote = CreditNote.Issue(shopId, Guid.NewGuid(), 50m, "Return", "CN-001", null).Value;

        var command = MakeCommand(shopId, actorId, inventoryBatchId: batch.Id) with
        {
            PaidAmount = 490m,
            CreditNoteAppliedAmount = 100m,
            CreditNoteRedemptions = [new CreditNoteRedemptionCommand(creditNote.Code, 100m)],
        };
        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);
        var itemNameById = new Dictionary<Guid, string> { { item.Id, item.Name } };
        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult(new List<ValidatedSaleLine> { line }, itemNameById));
        _creditNoteRepository.GetByCodeAsync(shopId, creditNote.Code, Arg.Any<CancellationToken>())
            .Returns(creditNote);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("CreditNote.InsufficientBalance", result.FirstError.Code);
        await _saleRepository.DidNotReceive().AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCreditNoteExpiredOrVoided_ReturnsError()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);
        var expiredCreditNote = CreditNote.Issue(shopId, Guid.NewGuid(), 100m, "Return", "CN-EX", DateTimeOffset.UtcNow.AddDays(-1)).Value;
        var voidedCreditNote = CreditNote.Issue(shopId, Guid.NewGuid(), 100m, "Return", "CN-VO", null).Value;
        voidedCreditNote.Void("voided");

        var command = MakeCommand(shopId, actorId, inventoryBatchId: batch.Id) with
        {
            PaidAmount = 490m,
            CreditNoteAppliedAmount = 100m,
            CreditNoteRedemptions = [new CreditNoteRedemptionCommand(expiredCreditNote.Code, 100m)],
        };
        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);
        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult(new List<ValidatedSaleLine> { line }, new Dictionary<Guid, string> { { item.Id, item.Name } }));

        _creditNoteRepository.GetByCodeAsync(shopId, expiredCreditNote.Code, Arg.Any<CancellationToken>())
            .Returns(expiredCreditNote);

        var expiredResult = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.True(expiredResult.IsError);
        Assert.Equal("CreditNote.ExpiredCannotRedeem", expiredResult.FirstError.Code);

        var voidCommand = command with
        {
            CreditNoteRedemptions = [new CreditNoteRedemptionCommand(voidedCreditNote.Code, 100m)],
        };
        _creditNoteRepository.GetByCodeAsync(shopId, voidedCreditNote.Code, Arg.Any<CancellationToken>())
            .Returns(voidedCreditNote);

        var voidResult = await CreateHandler().HandleAsync(voidCommand, CancellationToken.None);

        Assert.True(voidResult.IsError);
        Assert.Equal("CreditNote.VoidedCannotRedeem", voidResult.FirstError.Code);
        await _saleRepository.DidNotReceive().AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenIdempotencyKeyReused_ReturnsExistingSale()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var idempotencyKey = $"sale-{Guid.NewGuid():N}";
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var command = MakeCommand(shopId, actorId, inventoryBatchId: batch.Id, idempotencyKey: idempotencyKey);
        var requestHash = RecordSaleIdempotencyHasher.ComputeHash(command);

        var lineInput = new SaleLineInput(
            shopId,
            SaleLineType.Goods,
            item.Id,
            batch.Id,
            ServiceId: null,
            LineName: item.Name,
            LineCode: item.Barcode,
            command.Items[0].Quantity,
            batch.CostPrice,
            batch.SalesPrice,
            batch.Mrp,
            batch.TaxRatePercent,
            batch.TaxIncluded,
            HasPriceMismatch: false);

        var sale = Sale.Record(
            shopId,
            actorId,
            idempotencyKey,
            requestHash,
            "INV-TEST-01",
            [lineInput],
            command.CustomerId,
            command.CustomerName,
            command.CustomerPhone,
            command.PaymentMethod,
            command.PaidAmount,
            command.DueAmount,
            DateTimeOffset.UtcNow).Value;

        _saleRepository.GetByIdempotencyKeyAsync(shopId, actorId, idempotencyKey, Arg.Any<CancellationToken>())
            .Returns(sale);
        _itemRepository.GetByIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([item]);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(sale.Id, result.Value.SaleId);
        Assert.Single(result.Value.Items);
        Assert.Equal(item.Name, result.Value.Items[0].ItemName);
        await _saleLineValidator.DidNotReceive()
            .ValidateLinesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>());
        await _saleRepository.DidNotReceive().AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        await _stockTransactionRepository.DidNotReceive().AddAsync(Arg.Any<StockTransaction>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenIdempotencyKeyConflicts_ReturnsConflict()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var idempotencyKey = $"sale-{Guid.NewGuid():N}";
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var command = MakeCommand(shopId, actorId, inventoryBatchId: batch.Id, idempotencyKey: idempotencyKey);

        var lineInput = new SaleLineInput(
            shopId,
            SaleLineType.Goods,
            item.Id,
            batch.Id,
            ServiceId: null,
            LineName: item.Name,
            LineCode: item.Barcode,
            command.Items[0].Quantity,
            batch.CostPrice,
            batch.SalesPrice,
            batch.Mrp,
            batch.TaxRatePercent,
            batch.TaxIncluded,
            HasPriceMismatch: false);

        var sale = Sale.Record(
            shopId,
            actorId,
            idempotencyKey,
            "DIFFERENT-HASH",
            "INV-TEST-02",
            [lineInput],
            command.CustomerId,
            command.CustomerName,
            command.CustomerPhone,
            command.PaymentMethod,
            command.PaidAmount,
            command.DueAmount,
            DateTimeOffset.UtcNow).Value;

        _saleRepository.GetByIdempotencyKeyAsync(shopId, actorId, idempotencyKey, Arg.Any<CancellationToken>())
            .Returns(sale);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.IdempotencyConflict.Code, result.FirstError.Code);
        await _saleLineValidator.DidNotReceive()
            .ValidateLinesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>());
        await _saleRepository.DidNotReceive().AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        await _stockTransactionRepository.DidNotReceive().AddAsync(Arg.Any<StockTransaction>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ComputeHash_WhenCreditNoteRedemptionChanges_ProducesDifferentHash()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var batchId = Guid.NewGuid();

        var baseCommand = MakeCommand(shopId, actorId, inventoryBatchId: batchId) with
        {
            CreditNoteAppliedAmount = 100m,
            CreditNoteRedemptions = [new CreditNoteRedemptionCommand("CN-001", 100m)],
        };
        var changedCommand = baseCommand with
        {
            CreditNoteRedemptions = [new CreditNoteRedemptionCommand("CN-002", 100m)],
        };

        var baseHash = RecordSaleIdempotencyHasher.ComputeHash(baseCommand);
        var changedHash = RecordSaleIdempotencyHasher.ComputeHash(changedCommand);

        Assert.NotEqual(baseHash, changedHash);
    }

    [Fact]
    public async Task HandleAsync_WhenIdempotencyKeyReusedAndLineHsnCodeDiffers_ReturnsConflict()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var idempotencyKey = $"sale-{Guid.NewGuid():N}";
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var command = MakeCommand(shopId, actorId, inventoryBatchId: batch.Id, idempotencyKey: idempotencyKey, hsnCode: "0902");
        var requestHash = RecordSaleIdempotencyHasher.ComputeHash(command);

        var lineInput = new SaleLineInput(
            shopId,
            SaleLineType.Goods,
            item.Id,
            batch.Id,
            ServiceId: null,
            LineName: item.Name,
            LineCode: item.Barcode,
            command.Items[0].Quantity,
            batch.CostPrice,
            batch.SalesPrice,
            batch.Mrp,
            batch.TaxRatePercent,
            batch.TaxIncluded,
            HasPriceMismatch: false,
            HsnCode: "0902");

        var sale = Sale.Record(
            shopId,
            actorId,
            idempotencyKey,
            requestHash,
            "INV-TEST-05",
            [lineInput],
            command.CustomerId,
            command.CustomerName,
            command.CustomerPhone,
            command.PaymentMethod,
            command.PaidAmount,
            command.DueAmount,
            DateTimeOffset.UtcNow).Value;

        _saleRepository.GetByIdempotencyKeyAsync(shopId, actorId, idempotencyKey, Arg.Any<CancellationToken>())
            .Returns(sale);

        var changedLineCommand = MakeCommand(shopId, actorId, inventoryBatchId: batch.Id, idempotencyKey: idempotencyKey, hsnCode: "0903");
        var result = await CreateHandler().HandleAsync(changedLineCommand, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.IdempotencyConflict.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenSaveChangesThrowsAndSameHash_ReturnsExistingSale()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var idempotencyKey = $"sale-{Guid.NewGuid():N}";
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);
        var command = MakeCommand(shopId, actorId, inventoryBatchId: batch.Id, idempotencyKey: idempotencyKey);
        var requestHash = RecordSaleIdempotencyHasher.ComputeHash(command);

        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);
        var itemNameById = new Dictionary<Guid, string> { { item.Id, item.Name } };
        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult(new List<ValidatedSaleLine> { line }, itemNameById));

        var lineInput = new SaleLineInput(
            shopId,
            SaleLineType.Goods,
            item.Id,
            batch.Id,
            ServiceId: null,
            LineName: item.Name,
            LineCode: item.Barcode,
            command.Items[0].Quantity,
            batch.CostPrice,
            batch.SalesPrice,
            batch.Mrp,
            batch.TaxRatePercent,
            batch.TaxIncluded,
            HasPriceMismatch: false);

        var concurrentSale = Sale.Record(
            shopId,
            actorId,
            idempotencyKey,
            requestHash,
            "INV-TEST-03",
            [lineInput],
            command.CustomerId,
            command.CustomerName,
            command.CustomerPhone,
            command.PaymentMethod,
            command.PaidAmount,
            command.DueAmount,
            DateTimeOffset.UtcNow).Value;

        _saleRepository.GetByIdempotencyKeyAsync(shopId, actorId, idempotencyKey, Arg.Any<CancellationToken>())
            .Returns((Sale?)null, concurrentSale);
        _itemRepository.GetByIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([item]);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new DbUpdateException("Simulated failure")));

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(concurrentSale.Id, result.Value.SaleId);
        Assert.Single(result.Value.Items);
        Assert.Equal(item.Name, result.Value.Items[0].ItemName);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _saleRepository.Received(2).GetByIdempotencyKeyAsync(shopId, actorId, idempotencyKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenSaveChangesThrowsAndDifferentHash_ReturnsConflict()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var idempotencyKey = $"sale-{Guid.NewGuid():N}";
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);
        var command = MakeCommand(shopId, actorId, inventoryBatchId: batch.Id, idempotencyKey: idempotencyKey);

        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);
        var itemNameById = new Dictionary<Guid, string> { { item.Id, item.Name } };
        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult(new List<ValidatedSaleLine> { line }, itemNameById));

        var lineInput = new SaleLineInput(
            shopId,
            SaleLineType.Goods,
            item.Id,
            batch.Id,
            ServiceId: null,
            LineName: item.Name,
            LineCode: item.Barcode,
            command.Items[0].Quantity,
            batch.CostPrice,
            batch.SalesPrice,
            batch.Mrp,
            batch.TaxRatePercent,
            batch.TaxIncluded,
            HasPriceMismatch: false);

        var concurrentSale = Sale.Record(
            shopId,
            actorId,
            idempotencyKey,
            "DIFFERENT-HASH",
            "INV-TEST-04",
            [lineInput],
            command.CustomerId,
            command.CustomerName,
            command.CustomerPhone,
            command.PaymentMethod,
            command.PaidAmount,
            command.DueAmount,
            DateTimeOffset.UtcNow).Value;

        _saleRepository.GetByIdempotencyKeyAsync(shopId, actorId, idempotencyKey, Arg.Any<CancellationToken>())
            .Returns((Sale?)null, concurrentSale);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new DbUpdateException("Simulated failure")));

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.IdempotencyConflict.Code, result.FirstError.Code);
        await _itemRepository.DidNotReceive()
            .GetByIdsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenBarcodeNotFound_ReturnsError()
    {
        ErrorOr<SaleLineValidationResult> itemNotFound = Errors.Sale.ItemNotFound("BC-001");
        _saleLineValidator.ValidateLinesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(),
                Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(itemNotFound);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(MakeCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Sale.ItemNotFound", result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenBatchNotFound_ReturnsError()
    {
        ErrorOr<SaleLineValidationResult> batchNotFound = Errors.Sale.BatchNotFound("BC-001", "B-01");
        _saleLineValidator.ValidateLinesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(),
                Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(batchNotFound);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(MakeCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Sale.BatchNotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenInsufficientStock_ReturnsError()
    {
        ErrorOr<SaleLineValidationResult> insufficientStock = Errors.Sale.InsufficientStock("BC-001", "B-01");
        _saleLineValidator.ValidateLinesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(),
                Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(insufficientStock);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(MakeCommand(Guid.NewGuid(), Guid.NewGuid(), quantity: 5m), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Sale.InsufficientStock", result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WithMultipleItems_CallsSaveChangesOnce()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item1 = MakeItem(shopId, "BC-001", "Rice");
        var item2 = MakeItem(shopId, "BC-002", "Dal");
        var batch1 = MakeBatch(shopId, item1.Id, "B-01");
        var batch2 = MakeBatch(shopId, item2.Id, "B-02");
        var inv1 = MakeInventory(shopId, item1.Id);
        var inv2 = MakeInventory(shopId, item2.Id);

        var command = new RecordSaleCommand(actorId, shopId, $"sale-{Guid.NewGuid():N}", null, null, null, PaymentMethod.UPI, 905m, 0m,
            [new RecordSaleItemCommand("BC-001", "B-01", "Rice", 5m, 80m, 100m, 120m, 18m, false, batch1.Id),
             new RecordSaleItemCommand("BC-002", "B-02", "Dal", 3m, 60m, 80m, 100m, 5m, false, batch2.Id)]);

        var line1 = new ValidatedSaleLine(command.Items[0], item1, batch1, inv1, false);
        var line2 = new ValidatedSaleLine(command.Items[1], item2, batch2, inv2, false);

        var itemNameById = new Dictionary<Guid, string> { { item1.Id, "Rice" }, { item2.Id, "Dal" } };
        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult(new List<ValidatedSaleLine> { line1, line2 }, itemNameById));

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Items.Count);
        await _stockTransactionRepository.Received(2).AddAsync(Arg.Any<StockTransaction>(), Arg.Any<CancellationToken>());
        await _saleRepository.Received(1).AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenNoRegisteredCustomer_SavesGuestNameAndPhone()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);
        var command = new RecordSaleCommand(actorId, shopId, $"sale-{Guid.NewGuid():N}", null, "Guest Raj", "+919999999999", PaymentMethod.Cash, 590m, 0m,
            [new RecordSaleItemCommand("BC-001", "B-01", "Rice", 5m, 80m, 100m, 120m, 18m, false, batch.Id)]);
        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);

        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(),
                Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult(new List<ValidatedSaleLine> { line }, new Dictionary<Guid, string> { { item.Id, item.Name } }));

        Sale? capturedSale = null;
        await _saleRepository.AddAsync(Arg.Do<Sale>(s => capturedSale = s), Arg.Any<CancellationToken>());

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(capturedSale);
        Assert.Equal("Guest Raj", capturedSale!.CustomerName);
        Assert.Equal("+919999999999", capturedSale!.CustomerPhone);
    }

    [Fact]
    public async Task HandleAsync_WhenSecondLineBatchHasInsufficientStock_ReturnsErrorWithoutSaving()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var item1 = MakeItem(shopId, "BC-001", "Rice");
        var item2 = MakeItem(shopId, "BC-002", "Dal");
        var batch1 = MakeBatch(shopId, item1.Id, "B-01", quantity: 100m);
        var batch2 = MakeBatch(shopId, item2.Id, "B-02", quantity: 1m);
        var inv1 = MakeInventory(shopId, item1.Id);
        var inv2 = MakeInventory(shopId, item2.Id);

        var command = new RecordSaleCommand(actorId, shopId, $"sale-{Guid.NewGuid():N}", null, "Guest", "+911111111111", PaymentMethod.Cash, 905m, 0m,
            [
                new RecordSaleItemCommand("BC-001", "B-01", "Rice", 5m, 80m, 100m, 120m, 18m, false, batch1.Id),
                new RecordSaleItemCommand("BC-002", "B-02", "Dal", 3m, 60m, 80m, 100m, 5m, false, batch2.Id),
            ]);

        var line1 = new ValidatedSaleLine(command.Items[0], item1, batch1, inv1, false);
        var line2 = new ValidatedSaleLine(command.Items[1], item2, batch2, inv2, false);
        var itemNameById = new Dictionary<Guid, string> { { item1.Id, "Rice" }, { item2.Id, "Dal" } };

        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult(new List<ValidatedSaleLine> { line1, line2 }, itemNameById));

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("InventoryBatch.InsufficientStock", result.FirstError.Code);
        await _saleRepository.DidNotReceive().AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCustomerResolutionFails_ReturnsErrorWithoutSaving()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);

        var command = new RecordSaleCommand(actorId, shopId, $"sale-{Guid.NewGuid():N}", null, "Walk In", "+919999999999", PaymentMethod.Credit, 78m, 40m,
            [new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 80m, 100m, 120m, 18m, false, batch.Id)]);
        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);
        var itemNameById = new Dictionary<Guid, string> { { item.Id, item.Name } };

        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult(new List<ValidatedSaleLine> { line }, itemNameById));

        _customerResolver.ResolveAsync(shopId, command.CustomerId, command.CustomerPhone, true, PaymentMethod.Credit, Arg.Any<CancellationToken>())
            .Returns(Errors.Sale.CreditCustomerNotFound);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.CreditCustomerNotFound.Code, result.FirstError.Code);
        await _saleRepository.DidNotReceive().AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenPaidAndDueMismatch_DoesNotSaveChanges()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);

        // qty=1, price=100, tax=18% exclusive → total=118, but PaidAmount=999 → mismatch
        var command = new RecordSaleCommand(actorId, shopId, $"sale-{Guid.NewGuid():N}", null, "Ravi", "+919876543210",
            PaymentMethod.Cash, PaidAmount: 999m, DueAmount: 0m,
            [new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 80m, 100m, 120m, 18m, false, batch.Id)]);
        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);
        var itemNameById = new Dictionary<Guid, string> { { item.Id, item.Name } };

        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult(new List<ValidatedSaleLine> { line }, itemNameById));

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.PaidAndDueAmountMismatch.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCreditSaleWithResolvedCustomer_CreatesSaleAndLedgerEntry()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var customer = Customer.Create(shopId, "Reg User", "+911234567890", null, true);
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);

        // qty=1, price=100, tax=18% → total=118, paidAmount=78, dueAmount=40
        var command = new RecordSaleCommand(actorId, shopId, $"sale-{Guid.NewGuid():N}", customer.Id, null, null,
            PaymentMethod.Credit, PaidAmount: 78m, DueAmount: 40m,
            [new RecordSaleItemCommand("BC-001", "B-01", "Rice", 1m, 80m, 100m, 120m, 18m, false, batch.Id)]);
        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);

        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult(new List<ValidatedSaleLine> { line }, new Dictionary<Guid, string> { { item.Id, item.Name } }));
        _customerResolver.ResolveAsync(shopId, customer.Id, null, true, PaymentMethod.Credit, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Customer?>>(customer));

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        await _saleRepository.Received(1).AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        await _customerLedgerEntryRepository.Received(1).AddAsync(Arg.Any<CustomerLedgerEntry>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenPricingFailsBelowCost_DoesNotMutateOrSave()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);
        var command = MakeCommand(shopId, actorId);
        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);

        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult([line], new Dictionary<Guid, string> { { item.Id, item.Name } }));
        _salePricingCalculator.CalculateAsync(Arg.Any<SalePricingCalculationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Errors.Sale.LineWouldBeBelowCost(batch.Id));

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.LineWouldBeBelowCost(batch.Id).Code, result.FirstError.Code);
        Assert.Equal(100m, batch.Quantity);
        Assert.Equal(100m, inventory.Quantity);
        await _stockTransactionRepository.DidNotReceive().AddAsync(Arg.Any<StockTransaction>(), Arg.Any<CancellationToken>());
        await _saleRepository.DidNotReceive().AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PersistsConfiguredAndOverrideDiscountSnapshots()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);
        var command = MakeCommand(shopId, actorId) with
        {
            SaleDiscount = new Intelibill.Domain.ValueObjects.InstantDiscount(Intelibill.Domain.ValueObjects.InstantDiscountType.Percentage, 10m),
            Items = [MakeCommand(shopId, actorId, inventoryBatchId: batch.Id).Items[0] with
            {
                ItemDiscount = new Intelibill.Domain.ValueObjects.InstantDiscount(Intelibill.Domain.ValueObjects.InstantDiscountType.Flat, 2m),
            }],
            PaidAmount = 106.2m,
        };
        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);
        var configuredRuleId = Guid.NewGuid();
        var batchRuleId = Guid.NewGuid();

        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult([line], new Dictionary<Guid, string> { { item.Id, item.Name } }));
        _salePricingCalculator.CalculateAsync(Arg.Any<SalePricingCalculationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SalePricingCalculationResult(
                [
                    new SalePricingLineCalculation(batch.Id, 1m, 80m, 100m, 18m, false, 100m, 2m, 8m, 90m, 16.2m, 106.2m, 10m, 10m, batchRuleId, 5m),
                ],
                100m, 90m, 16.2m, 10m, 106.2m,
                new SalePricingConfiguredSaleRule(configuredRuleId, DiscountRuleType.SalePercentage, 8m, null),
                []));

        Sale? capturedSale = null;
        await _saleRepository.AddAsync(Arg.Do<Sale>(s => capturedSale = s), Arg.Any<CancellationToken>());

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(capturedSale);
        Assert.Equal(configuredRuleId, capturedSale!.ConfiguredSaleRuleId);
        Assert.Equal(Intelibill.Domain.ValueObjects.InstantDiscountType.Percentage, capturedSale.SaleDiscountOverrideType);
        Assert.Single(capturedSale.Items);
        Assert.Equal(batchRuleId, capturedSale.Items[0].ConfiguredBatchRuleId);
        Assert.Equal(Intelibill.Domain.ValueObjects.InstantDiscountType.Flat, capturedSale.Items[0].ItemDiscountOverrideType);
        Assert.Equal(2m, capturedSale.Items[0].ItemDiscountOverrideValue);
    }

    [Fact]
    public async Task HandleAsync_WhenCreditNoteConcurrencyConflict_ReturnsConflictError()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);
        var creditNote = CreditNote.Issue(shopId, Guid.NewGuid(), 500m, "Return", "CN-001", null).Value;

        var command = MakeCommand(shopId, actorId, inventoryBatchId: batch.Id) with
        {
            PaidAmount = 490m,
            CreditNoteAppliedAmount = 100m,
            CreditNoteRedemptions = [new CreditNoteRedemptionCommand(creditNote.Code, 100m)],
        };
        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);
        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult([line], new Dictionary<Guid, string> { { item.Id, item.Name } }));
        _creditNoteRepository.GetByCodeAsync(shopId, creditNote.Code, Arg.Any<CancellationToken>())
            .Returns(creditNote);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new DbUpdateConcurrencyException("Simulated concurrent update", new List<IUpdateEntry>())));

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.CreditNoteRedemptionConflict.Code, result.FirstError.Code);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenInventoryConcurrencyConflictWithoutCreditNote_RethrowsConcurrencyException()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "BC-001");
        var batch = MakeBatch(shopId, item.Id, "B-01");
        var inventory = MakeInventory(shopId, item.Id);
        var command = MakeCommand(shopId, actorId, inventoryBatchId: batch.Id);
        var line = new ValidatedSaleLine(command.Items[0], item, batch, inventory, false);

        _saleLineValidator.ValidateLinesAsync(shopId, Arg.Any<IReadOnlyList<RecordSaleItemCommand>>(), Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new SaleLineValidationResult([line], new Dictionary<Guid, string> { { item.Id, item.Name } }));
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new DbUpdateConcurrencyException("Simulated concurrent update", new List<IUpdateEntry>())));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => CreateHandler().HandleAsync(command, CancellationToken.None));
    }
}
