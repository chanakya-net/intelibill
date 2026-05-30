using ErrorOr;
using Intelibill.Application.Features.BankAccounts.Commands.DeleteBankAccount;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.BankAccounts.Commands.DeleteBankAccount;

public class DeleteBankAccountCommandHandlerTests
{
    private readonly IBankAccountRepository _bankAccountRepository = Substitute.For<IBankAccountRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_WithValidId_DeletesBankAccount()
    {
        var shopId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var existingAccount = BankAccount.Create(shopId, "Bank", "Acc", BankAccountType.Savings, null, null);
        
        _bankAccountRepository.GetByIdAsync(accountId, Arg.Any<CancellationToken>()).Returns(existingAccount);

        var handler = new DeleteBankAccountCommandHandler(_bankAccountRepository, _unitOfWork);

        var result = await handler.HandleAsync(new DeleteBankAccountCommand(accountId, shopId), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(Result.Deleted, result.Value);

        _bankAccountRepository.Received(1).Remove(existingAccount);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenAccountNotFound_ReturnsNotFoundError()
    {
        var accountId = Guid.NewGuid();
        _bankAccountRepository.GetByIdAsync(accountId, Arg.Any<CancellationToken>()).Returns((BankAccount?)null);

        var handler = new DeleteBankAccountCommandHandler(_bankAccountRepository, _unitOfWork);

        var result = await handler.HandleAsync(new DeleteBankAccountCommand(accountId, Guid.NewGuid()), CancellationToken.None);

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

        var handler = new DeleteBankAccountCommandHandler(_bankAccountRepository, _unitOfWork);

        var result = await handler.HandleAsync(new DeleteBankAccountCommand(accountId, wrongShopId), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("BankAccount.NotFound", result.FirstError.Code);
    }
}
