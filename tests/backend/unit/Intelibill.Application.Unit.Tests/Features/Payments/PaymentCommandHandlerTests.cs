using Intelibill.Application.Common.Posting;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Features.Payments.Commands.AllocatePayment;
using Intelibill.Application.Features.Payments.Commands.ReversePayment;
using Intelibill.Application.Features.Payments.DTOs;
using Intelibill.Application.Features.Payments.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Application.Unit.Tests.Common.Time;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Payments;

public class PaymentCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPaymentReceiptRepository _receipts = Substitute.For<IPaymentReceiptRepository>();
    private readonly IInvoiceBalanceReader _balances = Substitute.For<IInvoiceBalanceReader>();
    private readonly IFinancialOperationExecutor _executor = Substitute.For<IFinancialOperationExecutor>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly Shop _shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);

    private User Member(ShopRole role)
    {
        var user = User.CreateWithEmail($"{role}@test.com", "hash", role.ToString(), "User");
        user.AddShopMembership(ShopMembership.Create(_shop.Id, user.Id, role, role == ShopRole.Owner));
        _users.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        return user;
    }

    private PaymentReceipt StoredReceipt()
    {
        var receipt = PaymentReceipt.Record(
            _shop.Id, PaymentPartyType.Customer, Guid.NewGuid(), 100m, ReceiptTenderMethod.Cash, null, null,
            new DateOnly(2026, 11, 6), DateTimeOffset.UtcNow, null, Guid.NewGuid(), null, Guid.NewGuid()).Value;
        _receipts.GetByShopAndIdAsync(_shop.Id, receipt.Id, Arg.Any<CancellationToken>()).Returns(receipt);
        return receipt;
    }

    private PaymentReceiptDtoBuilder Builder() => new(
        _receipts, _balances, Substitute.For<ICustomerRepository>(), Substitute.For<ISupplierRepository>(), _users);

    private readonly IPostingPeriodPolicy _postingPeriods = Substitute.For<IPostingPeriodPolicy>();
    private readonly IExpenseRepository _expenses = Substitute.For<IExpenseRepository>();
    private ReversePaymentCommandHandler ReverseHandler() => new(
        _users, _receipts, Substitute.For<ICustomerLedgerEntryRepository>(), Substitute.For<ISupplierLedgerEntryRepository>(),
        _expenses, new PaymentTransactionRunner(_executor, _unitOfWork), Builder(), _unitOfWork,
        BusinessTestDoubles.AnyShop(), _postingPeriods, BusinessTestDoubles.RealClock(), BusinessTestDoubles.DisabledTillSettlement());

    [Fact]
    public async Task Reverse_SuspendedOwner_IsRefusedBeforeReplay()
    {
        var actor = Member(ShopRole.Owner);
        var receipt = StoredReceipt();
        actor.ShopMemberships.Single().ChangeAuthority(ShopRole.Owner, MembershipStatus.Suspended);

        var result = await ReverseHandler().HandleAsync(
            new ReversePaymentCommand(actor.Id, _shop.Id, receipt.Id, "Wrong customer", "rev-suspended"), CancellationToken.None);

        Assert.Empty(_executor.ReceivedCalls());
        Assert.True(result.IsError);
        Assert.Equal("Shop.MembershipNotFound", result.FirstError.Code);
    }

    [Theory]
    [InlineData(ShopRole.Manager)]
    [InlineData(ShopRole.Staff)]
    public async Task Reverse_NonOwner_IsForbiddenBeforeAnyOperation(ShopRole role)
    {
        var actor = Member(role);
        var receipt = StoredReceipt();

        var result = await ReverseHandler().HandleAsync(
            new ReversePaymentCommand(actor.Id, _shop.Id, receipt.Id, "Wrong customer", "rev-1"), CancellationToken.None);

        Assert.Equal(Errors.Payment.UserIsNotOwner.Code, result.FirstError.Code);
        Assert.Empty(_executor.ReceivedCalls());
    }

    [Fact]
    public async Task Reverse_UnknownReceipt_IsNotFound()
    {
        var owner = Member(ShopRole.Owner);

        var result = await ReverseHandler().HandleAsync(
            new ReversePaymentCommand(owner.Id, _shop.Id, Guid.NewGuid(), "Wrong customer", "rev-1"), CancellationToken.None);

        Assert.Equal(Errors.Payment.ReceiptNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Allocate_Staff_IsForbidden()
    {
        var staff = Member(ShopRole.Staff);
        var receipt = StoredReceipt();
        var handler = new AllocatePaymentCommandHandler(
            _users, _receipts, new PaymentAllocationPlanner(_balances), new PaymentTransactionRunner(_executor, _unitOfWork), Builder(), _unitOfWork);

        var result = await handler.HandleAsync(
            new AllocatePaymentCommand(staff.Id, _shop.Id, receipt.Id, [new PaymentAllocationRequest(Guid.NewGuid(), 10m)]),
            CancellationToken.None);

        Assert.Equal(Errors.Payment.UserIsNotOwnerOrManager.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Planner_RefusesDuplicateTargetsAndOverReceipt()
    {
        var receipt = StoredReceipt();
        var target = Guid.NewGuid();
        _balances.GetForCustomerAsync(_shop.Id, receipt.PartyId, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns([new CustomerInvoiceBalanceDto(target, "A/1", DateTimeOffset.UtcNow, 500m, 0m, 0m, 500m, null)]);
        var planner = new PaymentAllocationPlanner(_balances);

        var duplicate = await planner.PlanAsync(receipt, 0m,
            [new PaymentAllocationRequest(target, 10m), new PaymentAllocationRequest(target, 10m)], Guid.NewGuid(), CancellationToken.None);
        Assert.Equal("Payment.DuplicateAllocationTarget", duplicate.FirstError.Code);

        var over = await planner.PlanAsync(receipt, 60m, [new PaymentAllocationRequest(target, 50m)], Guid.NewGuid(), CancellationToken.None);
        Assert.Equal("Payment.AllocationExceedsReceipt", over.FirstError.Code);

        var ok = await planner.PlanAsync(receipt, 60m, [new PaymentAllocationRequest(target, 40m)], Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(40m, Assert.Single(ok.Value).Amount);
    }

    [Fact]
    public async Task Reverse_ClosedPeriod_IsRefusedBeforeAnyPartyLock()
    {
        var owner = Member(ShopRole.Owner);
        var receipt = StoredReceipt();
        _executor.ExecuteAsync(
                Arg.Any<FinancialOperationScope>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task<ErrorOr.ErrorOr<PaymentReceiptDto>>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<ErrorOr.ErrorOr<PaymentReceiptDto>>>>()(CancellationToken.None));
        _postingPeriods.AuthorizeForWriteAsync(_shop.Id, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr.ErrorOr<PostingPeriodLease>>(Errors.PostingPeriod.Closed(new DateOnly(2026, 6, 1))));

        var result = await ReverseHandler().HandleAsync(
            new ReversePaymentCommand(owner.Id, _shop.Id, receipt.Id, "Wrong customer", "rev-1"), CancellationToken.None);

        Assert.Equal("PostingPeriod.Closed", result.FirstError.Code);
        await _receipts.DidNotReceiveWithAnyArgs().LockPartyAndTargetsAsync(default, default, default, default!, default);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reverse_SupplierPaymentWhoseExpenseMonthIsClosed_ChangesNothing()
    {
        var owner = Member(ShopRole.Owner);
        var expense = Expense.Create(_shop.Id, Guid.NewGuid(), 100m, "Supplier", null, new DateOnly(2026, 6, 15), owner.Id);
        var receipt = PaymentReceipt.Record(
            _shop.Id, PaymentPartyType.Supplier, Guid.NewGuid(), 100m, ReceiptTenderMethod.Cash, null, null,
            new DateOnly(2026, 6, 15), DateTimeOffset.UtcNow, null, Guid.NewGuid(), expense.Id, owner.Id).Value;
        _receipts.GetByShopAndIdAsync(_shop.Id, receipt.Id, Arg.Any<CancellationToken>()).Returns(receipt);
        _expenses.GetByIdAsync(expense.Id, Arg.Any<CancellationToken>()).Returns(expense);
        _expenses.GetCorrectionsOfAsync(_shop.Id, expense.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<Expense>());
        _executor.ExecuteAsync(
                Arg.Any<FinancialOperationScope>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task<ErrorOr.ErrorOr<PaymentReceiptDto>>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<ErrorOr.ErrorOr<PaymentReceiptDto>>>>()(CancellationToken.None));
        _postingPeriods.AuthorizeForWriteAsync(
                _shop.Id,
                Arg.Is<IReadOnlyCollection<DateOnly>>(d => d.Count == 2 && d.Contains(new DateOnly(2026, 6, 15))),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr.ErrorOr<IReadOnlyList<PostingPeriodLease>>>(
                Errors.PostingPeriod.Closed(new DateOnly(2026, 6, 1))));

        var result = await ReverseHandler().HandleAsync(
            new ReversePaymentCommand(owner.Id, _shop.Id, receipt.Id, "Wrong supplier", "rev-1"), CancellationToken.None);

        Assert.Equal("PostingPeriod.Closed", result.FirstError.Code);
        Assert.False(expense.IsVoided);
        await _receipts.DidNotReceiveWithAnyArgs().LockPartyAndTargetsAsync(default, default, default, default!, default);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
