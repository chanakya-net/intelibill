using Intelibill.Application.Unit.Tests.Features.Shops;
using ErrorOr;
using Intelibill.Application.Features.BankAccounts.Commands.UpdateBankAccount;
using Intelibill.Application.Features.BankAccounts.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.BankAccounts.Commands.UpdateBankAccount;

public class UpdateBankAccountCommandHandlerTests
{
    private readonly IBankAccountRepository _bankAccountRepository = Substitute.For<IBankAccountRepository>();
    private readonly ShopSetupAccessFixture _setup = new();
    private IUnitOfWork _unitOfWork => _setup.UnitOfWork;

    [Fact]
    public async Task HandleAsync_WithValidData_UpdatesBankAccount()
    {
        var shopId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var existingAccount = BankAccount.Create(shopId, "Old Bank", "Old Acc", BankAccountType.Savings, "SBIN0001111", "Old Holder");
        typeof(BankAccount).GetProperty("Id")?.SetValue(existingAccount, accountId);

        _bankAccountRepository.GetByIdAsync(accountId, Arg.Any<CancellationToken>()).Returns(existingAccount);

        var handler = new UpdateBankAccountCommandHandler(_bankAccountRepository, _setup.Access);

        var result = await handler.HandleAsync(new UpdateBankAccountCommand(
            accountId,
            shopId,
            "New Bank",
            "New Acc",
            "Current",
            "SBIN0002222",
            "New Holder") { Session = _setup.Owner(shopId) }, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("New Bank", result.Value.BankName);
        Assert.Equal("New Acc", result.Value.AccountNumber);
        Assert.Equal("Current", result.Value.AccountType);

        _bankAccountRepository.Received(1).Update(existingAccount);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenAccountNotFound_ReturnsNotFoundError()
    {
        var shopId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        _bankAccountRepository.GetByIdAsync(accountId, Arg.Any<CancellationToken>()).Returns((BankAccount?)null);

        var handler = new UpdateBankAccountCommandHandler(_bankAccountRepository, _setup.Access);

        var result = await handler.HandleAsync(new UpdateBankAccountCommand(
            accountId, shopId, "Bank", "Acc", "Savings", null, null) { Session = _setup.Owner(shopId) }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("BankAccount.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenAccountBelongsToDifferentShop_ReturnsNotFoundError()
    {
        var accountId = Guid.NewGuid();
        var actualShopId = Guid.NewGuid();
        var wrongShopId = Guid.NewGuid();
        var existingAccount = BankAccount.Create(actualShopId, "Bank", "Acc", BankAccountType.Savings, null, null);
        typeof(BankAccount).GetProperty("Id")?.SetValue(existingAccount, accountId);

        _bankAccountRepository.GetByIdAsync(accountId, Arg.Any<CancellationToken>()).Returns(existingAccount);

        var handler = new UpdateBankAccountCommandHandler(_bankAccountRepository, _setup.Access);

        var result = await handler.HandleAsync(new UpdateBankAccountCommand(
            accountId, wrongShopId, "New Bank", "New Acc", "Savings", null, null) { Session = _setup.Owner(wrongShopId) }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("BankAccount.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WithEmptyRequiredFields_ReturnsValidationError()
    {
        var accountId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var existingAccount = BankAccount.Create(shopId, "Bank", "Acc", BankAccountType.Savings, null, null);
        _bankAccountRepository.GetByIdAsync(accountId, Arg.Any<CancellationToken>()).Returns(existingAccount);

        var handler = new UpdateBankAccountCommandHandler(_bankAccountRepository, _setup.Access);

        var result = await handler.HandleAsync(new UpdateBankAccountCommand(
            accountId, shopId, "", " ", "Savings", null, null) { Session = _setup.Owner(shopId) }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("BankAccount.Required", result.FirstError.Code);
    }
}
