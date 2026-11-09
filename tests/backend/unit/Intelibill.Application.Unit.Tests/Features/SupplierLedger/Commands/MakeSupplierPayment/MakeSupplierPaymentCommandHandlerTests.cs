using Intelibill.Application.Unit.Tests.Common.Time;
using Intelibill.Application.Common.Posting;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Features.SupplierLedger.DTOs;
using Intelibill.Application.Features.SupplierLedger.Commands.MakeSupplierPayment;
using Intelibill.Application.Features.Payments.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.SupplierLedger.Commands.MakeSupplierPayment;

public class MakeSupplierPaymentCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ISupplierRepository _supplierRepository = Substitute.For<ISupplierRepository>();
    private readonly ISupplierLedgerEntryRepository _ledgerRepository = Substitute.For<ISupplierLedgerEntryRepository>();
    private readonly IExpenseCategoryRepository _expenseCategoryRepository = Substitute.For<IExpenseCategoryRepository>();
    private readonly IExpenseRepository _expenseRepository = Substitute.For<IExpenseRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IFinancialOperationExecutor _executor = Substitute.For<IFinancialOperationExecutor>();

    private readonly IPaymentReceiptRepository _paymentReceiptRepository = Substitute.For<IPaymentReceiptRepository>();
    private readonly IBankAccountRepository _bankAccountRepository = Substitute.For<IBankAccountRepository>();
    private readonly IInvoiceBalanceReader _balanceReader = Substitute.For<IInvoiceBalanceReader>();

    public MakeSupplierPaymentCommandHandlerTests()
    {
        _paymentReceiptRepository
            .LockPartyAndTargetsAsync(Arg.Any<Guid>(), Arg.Any<PaymentPartyType>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(RowLockOutcome.Acquired);
    }

    private readonly IPostingPeriodPolicy _postingPeriods = Substitute.For<IPostingPeriodPolicy>();
    private MakeSupplierPaymentCommandHandler CreateHandler() =>
        new(_userRepository, _supplierRepository, _ledgerRepository, _expenseCategoryRepository, _expenseRepository,
            _paymentReceiptRepository, _bankAccountRepository, new PaymentAllocationPlanner(_balanceReader),
            new PaymentTransactionRunner(_executor, _unitOfWork), _postingPeriods, _unitOfWork,
            BusinessTestDoubles.DisabledTillPosting(), BusinessTestDoubles.RealClock(),
            NullLogger<MakeSupplierPaymentCommandHandler>.Instance);

    private static (User owner, Shop shop, Supplier supplier) BuildOwnerShopSupplier()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var membership = ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true);
        owner.AddShopMembership(membership);
        shop.AddMembership(membership);
        var supplier = Supplier.Create(shop.Id, "Fresh Foods", null, null, "42 MG Road", "Bengaluru", "Karnataka", "560001", true, false);
        return (owner, shop, supplier);
    }

    private static MakeSupplierPaymentCommand BuildCommand(Guid actorId, Guid shopId, Guid supplierId, decimal amount = 500m) =>
        new(actorId, shopId, supplierId, amount, DateOnly.FromDateTime(DateTime.UtcNow), null);

    [Fact]
    public async Task HandleAsync_OwnerRole_ReturnsSuccessDto()
    {
        var (owner, shop, supplier) = BuildOwnerShopSupplier();
        var category = ExpenseCategory.Create(shop.Id, "Supplier Payments", DateTimeOffset.UtcNow);
        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _supplierRepository.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);
        _expenseCategoryRepository.GetByNameAsync(shop.Id, "Supplier Payments", Arg.Any<CancellationToken>()).Returns(category);

        var result = await CreateHandler().HandleAsync(BuildCommand(owner.Id, shop.Id, supplier.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(supplier.Id, result.Value.SupplierId);
        await _ledgerRepository.Received(1).AddAsync(Arg.Is<SupplierLedgerEntry>(e =>
            e.SupplierId == supplier.Id &&
            e.EntryType == SupplierLedgerEntryType.PaymentMade &&
            e.Amount == 500m), Arg.Any<CancellationToken>());
        await _expenseRepository.Received(1).AddAsync(Arg.Is<Expense>(e =>
            e.ShopId == shop.Id &&
            e.CategoryId == category.Id &&
            e.Amount == 500m &&
            e.PaidTo == "Supplier" &&
            e.SupplierLedgerEntryId.HasValue), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_MissingCategory_CreatesCategoryAndExpenseInSameSave()
    {
        var (owner, shop, supplier) = BuildOwnerShopSupplier();
        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _supplierRepository.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);
        _expenseCategoryRepository.GetByNameAsync(shop.Id, "Supplier Payments", Arg.Any<CancellationToken>()).Returns((ExpenseCategory?)null);

        var result = await CreateHandler().HandleAsync(BuildCommand(owner.Id, shop.Id, supplier.Id), CancellationToken.None);

        Assert.False(result.IsError);
        await _expenseCategoryRepository.Received(1).AddAsync(Arg.Is<ExpenseCategory>(c =>
            c.ShopId == shop.Id &&
            c.Name == "Supplier Payments"), Arg.Any<CancellationToken>());
        await _expenseRepository.Received(1).AddAsync(Arg.Is<Expense>(e =>
            e.ShopId == shop.Id &&
            e.PaidTo == "Supplier" &&
            e.SupplierLedgerEntryId == result.Value.Id), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ManagerRole_ReturnsSuccess()
    {
        var (owner, shop, supplier) = BuildOwnerShopSupplier();
        var manager = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        manager.AddShopMembership(ShopMembership.Create(shop.Id, manager.Id, ShopRole.Manager, false));

        _userRepository.GetByIdWithDetailsAsync(manager.Id, Arg.Any<CancellationToken>()).Returns(manager);
        _supplierRepository.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);

        var result = await CreateHandler().HandleAsync(BuildCommand(manager.Id, shop.Id, supplier.Id), CancellationToken.None);

        Assert.False(result.IsError);
    }

    [Fact]
    public async Task HandleAsync_StaffRole_ReturnsForbidden()
    {
        var (owner, shop, supplier) = BuildOwnerShopSupplier();
        var staff = User.CreateWithEmail("staff@test.com", "hash", "Staff", "User");
        staff.AddShopMembership(ShopMembership.Create(shop.Id, staff.Id, ShopRole.Staff, false));

        _userRepository.GetByIdWithDetailsAsync(staff.Id, Arg.Any<CancellationToken>()).Returns(staff);

        var result = await CreateHandler().HandleAsync(BuildCommand(staff.Id, shop.Id, supplier.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.UserIsNotOwnerOrManager.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_ActorNotFound_ReturnsUserNotFound()
    {
        var actorId = Guid.NewGuid();
        _userRepository.GetByIdWithDetailsAsync(actorId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await CreateHandler().HandleAsync(BuildCommand(actorId, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.UserNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_MembershipNotFound_ReturnsMembershipError()
    {
        var actor = User.CreateWithEmail("actor@test.com", "hash", "Actor", "User");
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var result = await CreateHandler().HandleAsync(BuildCommand(actor.Id, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_SupplierNotFound_ReturnsSupplierNotFound()
    {
        var (owner, shop, _) = BuildOwnerShopSupplier();
        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _supplierRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Supplier?)null);

        var result = await CreateHandler().HandleAsync(BuildCommand(owner.Id, shop.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.SupplierNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_SupplierBelongsToDifferentShop_ReturnsSupplierNotFound()
    {
        var (owner, shop, _) = BuildOwnerShopSupplier();
        var foreignSupplier = Supplier.Create(Guid.NewGuid(), "Other Foods", null, null, "1 Main St", "Chennai", "TN", "600001", true, false);

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _supplierRepository.GetByIdAsync(foreignSupplier.Id, Arg.Any<CancellationToken>()).Returns(foreignSupplier);

        var result = await CreateHandler().HandleAsync(BuildCommand(owner.Id, shop.Id, foreignSupplier.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.SupplierNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WithOperationKey_RunsLedgerAndExpenseThroughExecutor()
    {
        var (owner, shop, supplier) = BuildOwnerShopSupplier();
        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _supplierRepository.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);
        _executor.ExecuteAsync(
                Arg.Any<FinancialOperationScope>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task<ErrorOr<SupplierLedgerEntryDto>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<ErrorOr<SupplierLedgerEntryDto>>>>()(CancellationToken.None));

        var command = BuildCommand(owner.Id, shop.Id, supplier.Id) with { OperationKey = "supplier-pay-1" };
        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        await _executor.Received(1).ExecuteAsync(
            new FinancialOperationScope(shop.Id, FinancialOperationType.SupplierPayment, "supplier-pay-1"),
            owner.Id,
            supplier.Id,
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<ErrorOr<SupplierLedgerEntryDto>>>>(),
            Arg.Any<CancellationToken>());
        await _ledgerRepository.Received(1).AddAsync(Arg.Any<SupplierLedgerEntry>(), Arg.Any<CancellationToken>());
        await _expenseRepository.Received(1).AddAsync(Arg.Any<Expense>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SupplierInOtherShopWithOperationKey_NeverReachesReceipt()
    {
        var (owner, shop, _) = BuildOwnerShopSupplier();
        var foreign = Supplier.Create(Guid.NewGuid(), "Other", null, null, "1 Road", "Pune", "Maharashtra", "411001", true, false);
        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _supplierRepository.GetByIdAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var command = BuildCommand(owner.Id, shop.Id, foreign.Id) with { OperationKey = "supplier-pay-1" };
        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.Equal(Errors.Supplier.SupplierNotFound.Code, result.FirstError.Code);
        Assert.Empty(_executor.ReceivedCalls());
    }

    [Fact]
    public async Task HandleAsync_PostsReceiptLinkedToLedgerEntryAndExpense()
    {
        var (owner, shop, supplier) = BuildOwnerShopSupplier();
        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _supplierRepository.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);
        Expense? expense = null;
        await _expenseRepository.AddAsync(Arg.Do<Expense>(e => expense = e), Arg.Any<CancellationToken>());

        var command = BuildCommand(owner.Id, shop.Id, supplier.Id, 700m) with { Method = ReceiptTenderMethod.BankTransfer };
        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(expense);
        await _paymentReceiptRepository.Received(1).AddAsync(
            Arg.Is<PaymentReceipt>(r =>
                r.Id == result.Value.ReceiptId
                && r.LedgerEntryId == result.Value.Id
                && r.ExpenseId == expense!.Id
                && r.PartyType == PaymentPartyType.Supplier
                && r.Method == ReceiptTenderMethod.BankTransfer),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClosedPeriod_RefusesPaymentOnItsPaymentDate_BeforeAnyLock()
    {
        var (owner, shop, supplier) = BuildOwnerShopSupplier();
        var paymentDate = new DateOnly(2026, 6, 25);
        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _supplierRepository.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);
        _postingPeriods.AuthorizeForWriteAsync(shop.Id, paymentDate, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr.ErrorOr<PostingPeriodLease>>(Errors.PostingPeriod.Closed(new DateOnly(2026, 6, 1))));

        var result = await CreateHandler().HandleAsync(
            BuildCommand(owner.Id, shop.Id, supplier.Id) with { PaymentDate = paymentDate }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("PostingPeriod.Closed", result.FirstError.Code);
        await _paymentReceiptRepository.DidNotReceiveWithAnyArgs().LockPartyAndTargetsAsync(default, default, default, default!, default);
        await _ledgerRepository.DidNotReceive().AddAsync(Arg.Any<SupplierLedgerEntry>(), Arg.Any<CancellationToken>());
        await _expenseRepository.DidNotReceive().AddAsync(Arg.Any<Expense>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
