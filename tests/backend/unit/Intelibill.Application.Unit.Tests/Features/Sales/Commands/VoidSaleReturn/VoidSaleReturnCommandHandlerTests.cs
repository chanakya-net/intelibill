using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Unit.Tests.Common;
using Intelibill.Application.Features.Tills;
using Intelibill.Application.Features.Tills.Services;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.VoidSaleReturn;
using Intelibill.Application.Features.Sales.Services.Returns;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Intelibill.Application.Unit.Tests.Common.Time;
using Intelibill.Infrastructure.Services.Time;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Commands.VoidSaleReturn;

public sealed class VoidSaleReturnCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPostingPeriodPolicy _postingPeriods = Substitute.For<IPostingPeriodPolicy>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly ISaleReturnRepository _saleReturnRepository = Substitute.For<ISaleReturnRepository>();
    private readonly IInventoryRepository _inventoryRepository = Substitute.For<IInventoryRepository>();
    private readonly IInventoryBatchRepository _inventoryBatchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IStockTransactionRepository _stockTransactionRepository = Substitute.For<IStockTransactionRepository>();
    private readonly ICustomerLedgerEntryRepository _customerLedgerEntryRepository = Substitute.For<ICustomerLedgerEntryRepository>();
    private readonly ICreditNoteRepository _creditNoteRepository = Substitute.For<ICreditNoteRepository>();
    private readonly IFinancialOperationExecutor _executor = Substitute.For<IFinancialOperationExecutor>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private VoidSaleReturnCommandHandler CreateHandler() =>
        new(
            _userRepository,
            _shopRepository,
            _saleRepository,
            _saleReturnRepository,
            _inventoryRepository,
            _inventoryBatchRepository,
            _stockTransactionRepository,
            _customerLedgerEntryRepository,
            _creditNoteRepository,
            new SaleReturnMutationCoordinator(_saleRepository, _shopRepository, _postingPeriods, new BusinessClock(_time), _unitOfWork, Substitute.For<IExpiryPolicyFence>()),
            _unitOfWork,
            new BusinessClock(_time),
            new TillSourceSettlementService(Substitute.For<ITillSessionPolicy>(), Substitute.For<ITillPostingService>(), _shopRepository, new BusinessClock(_time)),
            _executor, ExpiryDispositionTestServices.Create(_inventoryBatchRepository, new BusinessClock(_time)));

    private readonly MutableTimeProvider _time = new();

    [Fact]
    public async Task HandleAsync_VoidsOnTheBusinessDateOfTheVoidInstant()
    {
        _time.Now = DateTimeOffset.Parse("2026-10-20T20:30:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var fixture = Arrange(ShopRole.Owner, returnDueReductionAmount: 75m);
        CustomerLedgerEntry? ledger = null;
        await _customerLedgerEntryRepository.AddAsync(Arg.Do<CustomerLedgerEntry>(e => ledger = e), Arg.Any<CancellationToken>());

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, "Duplicate return"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(_time.Now, fixture.SaleReturn.VoidedAt);
        Assert.Equal(new DateOnly(2026, 10, 21), fixture.SaleReturn.VoidedBusinessDate);
        Assert.Equal(new DateOnly(2026, 10, 21), ledger!.EntryDate);
    }

    [Fact]
    public async Task HandleAsync_VoidsOnTheBusinessDateInTheShopsCurrentZone()
    {
        // 20:30 UTC on 20 Oct is 21 Oct in Kolkata (the zone the return was posted under) but
        // 20 Oct in New York (the shop's zone now, the one the period fence uses).
        _time.Now = DateTimeOffset.Parse("2026-10-20T20:30:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var fixture = Arrange(ShopRole.Owner, returnDueReductionAmount: 75m);
        typeof(Shop).GetProperty(nameof(Shop.BusinessTimeZoneId))!.SetValue(fixture.Shop, "America/New_York");

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, "Duplicate return"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(new DateOnly(2026, 10, 20), fixture.SaleReturn.VoidedBusinessDate);
    }

    [Theory]
    [InlineData(ShopRole.Owner)]
    [InlineData(ShopRole.Manager)]
    public async Task HandleAsync_ForOwnerOrManager_VoidsReturnAndCreatesCompensatingEffects(ShopRole role)
    {
        var fixture = Arrange(role, returnDueReductionAmount: 75m);

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, "Duplicate return"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(fixture.SaleReturn.IsVoided);
        Assert.Equal("Duplicate return", fixture.SaleReturn.VoidReason);
        Assert.Equal(8m, fixture.Batch.Quantity);
        Assert.Equal(18m, fixture.Inventory.Quantity);
        await _stockTransactionRepository.Received(1).AddAsync(
            Arg.Is<StockTransaction>(tx =>
                tx.TransactionType == StockTransactionType.Out
                && tx.Quantity == -2m
                && tx.ReferenceNumber == fixture.SaleReturn.ReturnNumber),
            Arg.Any<CancellationToken>());
        await _customerLedgerEntryRepository.Received(1).AddAsync(
            Arg.Is<CustomerLedgerEntry>(entry =>
                entry.EntryType == CustomerLedgerEntryType.ReturnCreditReversal
                && entry.Amount == 75m
                && entry.SaleId == fixture.Sale.Id
                && entry.CustomerId == fixture.Sale.CustomerId),
            Arg.Any<CancellationToken>());
        _saleReturnRepository.Received(1).Update(fixture.SaleReturn);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_TakesSaleLockBeforeReadingReturnOrCreditNote()
    {
        var fixture = Arrange(ShopRole.Owner);

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, "Duplicate return"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Received.InOrder(() =>
        {
            _saleReturnRepository.GetSaleIdAsync(fixture.Shop.Id, fixture.SaleReturn.Id, Arg.Any<CancellationToken>());
            _saleRepository.LockForReturnMutationAsync(fixture.Shop.Id, fixture.Sale.Id, Arg.Any<CancellationToken>());
            _saleReturnRepository.GetByIdWithItemsAsync(fixture.Shop.Id, fixture.SaleReturn.Id, Arg.Any<CancellationToken>());
            _creditNoteRepository.GetBySaleReturnIdForUpdateWithRedemptionsAsync(fixture.Shop.Id, fixture.SaleReturn.Id, Arg.Any<CancellationToken>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _unitOfWork.CommitTransactionAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task HandleAsync_WhenReturnNotInCallersShop_ReturnsNotFoundWithoutLocking()
    {
        var fixture = Arrange(ShopRole.Owner);
        var otherReturnId = Guid.NewGuid();

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, otherReturnId, "Duplicate return"),
            CancellationToken.None);

        Assert.Equal(Errors.Sale.ReturnNotFound(otherReturnId).Code, result.FirstError.Code);
        await _saleRepository.DidNotReceive().LockForReturnMutationAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ForStaff_IsForbiddenWithoutLocking()
    {
        var fixture = Arrange(ShopRole.Staff);

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, "Duplicate return"),
            CancellationToken.None);

        Assert.Equal(Errors.Sale.ReturnForbidden.Code, result.FirstError.Code);
        await _saleRepository.DidNotReceive().LockForReturnMutationAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenSaleLockTimesOut_ReturnsBusyWithoutVoiding()
    {
        var fixture = Arrange(ShopRole.Owner, returnDueReductionAmount: 75m);
        _saleRepository.LockForReturnMutationAsync(fixture.Shop.Id, fixture.Sale.Id, Arg.Any<CancellationToken>())
            .Returns(RowLockOutcome.TimedOut);

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, "Duplicate return"),
            CancellationToken.None);

        Assert.Equal(Errors.Sale.ReturnSaleBusy.Code, result.FirstError.Code);
        Assert.False(fixture.SaleReturn.IsVoided);
        await _customerLedgerEntryRepository.DidNotReceive().AddAsync(Arg.Any<CustomerLedgerEntry>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).RollbackTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCreditNoteBalanceSpentButRedemptionsNotYetVisible_ReturnsConflict()
    {
        var fixture = Arrange(ShopRole.Owner);
        var creditNote = CreditNote.Issue(
            fixture.Shop.Id,
            fixture.SaleReturn.Id,
            amount: 200m,
            reason: "Store credit for return",
            code: "CN-TEST-STALE",
            expiresAt: null).Value;
        // The locked row is current, the joined redemptions are not: only the balance shows the spend.
        typeof(CreditNote).GetProperty(nameof(CreditNote.AvailableBalance))!.SetValue(creditNote, 50m);
        _creditNoteRepository
            .GetBySaleReturnIdForUpdateWithRedemptionsAsync(fixture.Shop.Id, fixture.SaleReturn.Id, Arg.Any<CancellationToken>())
            .Returns(creditNote);

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, "Duplicate return"),
            CancellationToken.None);

        Assert.Equal(Errors.Sale.ReturnCreditNoteHasRedemptions.Code, result.FirstError.Code);
        Assert.False(creditNote.IsVoided);
        Assert.False(fixture.SaleReturn.IsVoided);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenAlreadyVoided_ReturnsValidationError()
    {
        var fixture = Arrange(ShopRole.Owner);
        fixture.SaleReturn.Void(DateTimeOffset.UtcNow, global::Intelibill.Domain.Common.BusinessCalendar.DateOf(DateTimeOffset.UtcNow), fixture.User.Id, "Earlier void");

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, "Duplicate return"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.ReturnAlreadyVoided.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenReasonMissing_ReturnsValidationError()
    {
        var fixture = Arrange(ShopRole.Owner);

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, " "),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.ReturnVoidReasonRequired.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCompensationWouldMakeStockNegative_ReturnsConflict()
    {
        var fixture = Arrange(ShopRole.Owner, batchQuantity: 1m, inventoryQuantity: 1m);

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, "Duplicate return"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.ReturnVoidInsufficientStock.Code, result.FirstError.Code);
        Assert.False(fixture.SaleReturn.IsVoided);
        await _stockTransactionRepository.DidNotReceive().AddAsync(
            Arg.Any<StockTransaction>(),
            Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ForWastageOnlyReturn_DoesNotCreateStockOrLedgerCompensation()
    {
        var fixture = Arrange(
            ShopRole.Owner,
            condition: SaleReturnCondition.Wastage,
            returnDueReductionAmount: 0m);

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, "Wrong return"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(fixture.SaleReturn.IsVoided);
        Assert.Equal(10m, fixture.Batch.Quantity);
        Assert.Equal(20m, fixture.Inventory.Quantity);
        await _stockTransactionRepository.DidNotReceive().AddAsync(
            Arg.Any<StockTransaction>(),
            Arg.Any<CancellationToken>());
        await _customerLedgerEntryRepository.DidNotReceive().AddAsync(
            Arg.Any<CustomerLedgerEntry>(),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithUnredeemedCreditNote_AutoVoidsNoteAndReturn()
    {
        var fixture = Arrange(ShopRole.Owner);
        var creditNote = CreditNote.Issue(
            fixture.Shop.Id,
            fixture.SaleReturn.Id,
            amount: 200m,
            reason: "Store credit for return",
            code: "CN-TEST-001",
            expiresAt: null).Value;
        _creditNoteRepository
            .GetBySaleReturnIdForUpdateWithRedemptionsAsync(fixture.Shop.Id, fixture.SaleReturn.Id, Arg.Any<CancellationToken>())
            .Returns(creditNote);

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, "Duplicate return"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(fixture.SaleReturn.IsVoided);
        Assert.True(creditNote.IsVoided);
        Assert.Equal("Duplicate return", creditNote.VoidReason);
        _creditNoteRepository.Received(1).Update(creditNote);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithAlreadyVoidedCreditNote_DoesNotFail()
    {
        var fixture = Arrange(ShopRole.Owner);
        var creditNote = CreditNote.Issue(
            fixture.Shop.Id,
            fixture.SaleReturn.Id,
            amount: 200m,
            reason: "Store credit for return",
            code: "CN-TEST-003",
            expiresAt: null).Value;
        creditNote.Void("Previously voided");
        _creditNoteRepository
            .GetBySaleReturnIdForUpdateWithRedemptionsAsync(fixture.Shop.Id, fixture.SaleReturn.Id, Arg.Any<CancellationToken>())
            .Returns(creditNote);

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, "Duplicate return"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(fixture.SaleReturn.IsVoided);
        Assert.True(creditNote.IsVoided);
        Assert.Equal("Previously voided", creditNote.VoidReason);
        _creditNoteRepository.DidNotReceive().Update(creditNote);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithRedeemedCreditNote_ReturnsConflict()
    {
        var fixture = Arrange(ShopRole.Owner);
        var creditNote = CreditNote.Issue(
            fixture.Shop.Id,
            fixture.SaleReturn.Id,
            amount: 200m,
            reason: "Store credit for return",
            code: "CN-TEST-002",
            expiresAt: null).Value;
        var redemptionSaleId = Guid.NewGuid();
        creditNote.Redeem(fixture.Shop.Id, redemptionSaleId, 50m);
        _creditNoteRepository
            .GetBySaleReturnIdForUpdateWithRedemptionsAsync(fixture.Shop.Id, fixture.SaleReturn.Id, Arg.Any<CancellationToken>())
            .Returns(creditNote);

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, "Duplicate return"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.ReturnCreditNoteHasRedemptions.Code, result.FirstError.Code);
        Assert.False(fixture.SaleReturn.IsVoided);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private VoidSaleReturnFixture Arrange(
        ShopRole role,
        SaleReturnCondition condition = SaleReturnCondition.Restockable,
        decimal returnDueReductionAmount = 0m,
        decimal batchQuantity = 10m,
        decimal inventoryQuantity = 20m)
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Shop", "Address", "City", "State", "560001", null, null, null);
        var customerId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var batch = InventoryBatch.Create(
            shop.Id,
            itemId,
            "B-001",
            batchQuantity,
            costPrice: 80m,
            mrp: 120m,
            salesPrice: 100m,
            taxRatePercent: 10m,
            taxIncluded: false,
            expiryDate: null,
            manufacturingDate: null,
            supplierId: null,
            user.Id).Value;
        var inventory = Intelibill.Domain.Entities.Inventory.Create(
            shop.Id,
            itemId,
            inventoryQuantity,
            reorderLevel: 0m,
            maxLevel: 100m,
            createdBy: user.Id).Value;
        var saleItem = SaleItem.CreateGoods(
            shop.Id,
            itemId,
            batch.Id,
            lineName: "Item",
            lineCode: "BC-001",
            quantity: 5m,
            costPrice: 80m,
            salesPrice: 100m,
            mrp: 120m,
            taxRatePercent: 10m,
            isPriceIncludingTax: false,
            hasPriceMismatch: false);
        var sale = Sale.Create(
            shop.Id,
            "INV-001",
            customerId,
            "Customer",
            null,
            PaymentMethod.Credit,
            DateTimeOffset.UtcNow,
            paidAmount: 300m,
            dueAmount: 250m,
            totalAmount: 550m,
            totalTaxAmount: 50m,
            [saleItem]);
        var returnItem = SaleReturnItem.Create(
            shop.Id,
            sale.Id,
            saleItem.Id,
            quantity: 2m,
            condition,
            originalCostPrice: 80m,
            originalSalesPrice: 100m,
            originalTaxRatePercent: 10m,
            originalIsPriceIncludingTax: false,
            maxRefundAmount: 220m,
            approvedRefundAmount: 220m,
            taxableAmount: 200m,
            taxAmount: 20m,
            notes: condition == SaleReturnCondition.Wastage ? "Damaged" : "Sealed").Value;
        var saleReturnLine = new SaleReturnLineInput(
            returnItem.ShopId,
            returnItem.SaleItemId,
            returnItem.Quantity,
            returnItem.Condition,
            returnItem.OriginalCostPrice,
            returnItem.OriginalSalesPrice,
            returnItem.OriginalTaxRatePercent,
            returnItem.OriginalIsPriceIncludingTax,
            returnItem.MaxRefundAmount,
            returnItem.ApprovedRefundAmount,
            returnItem.TaxableAmount,
            returnItem.TaxAmount,
            returnItem.Notes);

        var saleReturn = SaleReturn.Record(
            shop.Id,
            sale.Id,
            "RET-20260505-VOID01",
            DateTimeOffset.UtcNow,
            global::Intelibill.Domain.ValueObjects.BusinessDay.InDefaultZone(DateTimeOffset.UtcNow),
            user.Id,
            notes: null,
            totalRefundAmount: 220m,
            dueReductionAmount: returnDueReductionAmount,
            payoutAmount: 220m - returnDueReductionAmount,
            payoutDestination: ReturnPayoutDestination.Refund,
            totalTaxableAmount: 200m,
            totalTaxAmount: 20m,
            customerBalanceBefore: 250m,
            customerBalanceAfter: 250m - returnDueReductionAmount,
            [saleReturnLine]).Value;

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shop.Id, user.Id, role, true));
        _saleReturnRepository.GetByIdWithItemsAsync(shop.Id, saleReturn.Id, Arg.Any<CancellationToken>())
            .Returns(saleReturn);
        _saleRepository.GetByIdAsync(sale.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(sale);
        _saleReturnRepository.GetSaleIdAsync(shop.Id, saleReturn.Id, Arg.Any<CancellationToken>()).Returns(sale.Id);
        _saleRepository.LockForReturnMutationAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>()).Returns(RowLockOutcome.Acquired);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);
        _creditNoteRepository
            .GetBySaleReturnIdForUpdateWithRedemptionsAsync(shop.Id, saleReturn.Id, Arg.Any<CancellationToken>())
            .Returns((CreditNote?)null);

        return new VoidSaleReturnFixture(user, shop, sale, saleReturn, batch, inventory);
    }

    private sealed record VoidSaleReturnFixture(
        User User,
        Shop Shop,
        Sale Sale,
        SaleReturn SaleReturn,
        InventoryBatch Batch,
        Intelibill.Domain.Entities.Inventory Inventory);

    [Fact]
    public async Task ClosedPeriod_RefusesVoid_BeforeTheSaleLock()
    {
        _time.Now = DateTimeOffset.Parse("2026-10-20T20:30:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var fixture = Arrange(ShopRole.Owner, returnDueReductionAmount: 75m);
        _postingPeriods.AuthorizeForWriteAsync(fixture.Shop.Id, new DateOnly(2026, 10, 21), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr.ErrorOr<PostingPeriodLease>>(Errors.PostingPeriod.Closed(new DateOnly(2026, 10, 1))));

        var result = await CreateHandler().HandleAsync(
            new VoidSaleReturnCommand(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, "Duplicate return"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("PostingPeriod.Closed", result.FirstError.Code);
        await _saleRepository.DidNotReceive().LockForReturnMutationAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Assert.False(fixture.SaleReturn.IsVoided);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task KeyedVoid_SuspendedOwner_IsRefusedBeforeReplay()
    {
        var fixture = Arrange(ShopRole.Owner);
        var member = ShopMembership.Create(fixture.Shop.Id, fixture.User.Id, ShopRole.Owner, true);
        member.ChangeAuthority(ShopRole.Owner, MembershipStatus.Suspended);
        _shopRepository.GetMembershipAsync(fixture.User.Id, fixture.Shop.Id, Arg.Any<CancellationToken>()).Returns(member);
        var result = await CreateHandler().HandleAsync(new(fixture.User.Id, fixture.Shop.Id, fixture.SaleReturn.Id, "Error", OperationKey: "committed-key"), default);
        Assert.Equal("Shop.MembershipNotFound", result.FirstError.Code);
        Assert.Empty(_executor.ReceivedCalls());
    }

}
