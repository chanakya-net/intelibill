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

    private ReversePaymentCommandHandler ReverseHandler() => new(
        _users, _receipts, Substitute.For<ICustomerLedgerEntryRepository>(), Substitute.For<ISupplierLedgerEntryRepository>(),
        Substitute.For<IExpenseRepository>(), new PaymentTransactionRunner(_executor, _unitOfWork), Builder(), _unitOfWork,
        BusinessTestDoubles.AnyShop(), BusinessTestDoubles.RealClock());

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
}
