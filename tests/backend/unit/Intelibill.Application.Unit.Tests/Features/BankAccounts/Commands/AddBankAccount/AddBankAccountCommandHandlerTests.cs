using ErrorOr;
using Intelibill.Application.Features.BankAccounts.Commands.AddBankAccount;
using Intelibill.Application.Features.BankAccounts.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.BankAccounts.Commands.AddBankAccount;

public class AddBankAccountCommandHandlerTests
{
    private readonly IBankAccountRepository _bankAccountRepository = Substitute.For<IBankAccountRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_WithValidData_AddsBankAccount()
    {
        var shopId = Guid.NewGuid();
        var handler = new AddBankAccountCommandHandler(_bankAccountRepository, _unitOfWork);

        var result = await handler.HandleAsync(new AddBankAccountCommand(
            shopId,
            "State Bank of India",
            "123456789012",
            "Savings",
            "SBIN0001234",
            "Chandra Kumar"), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("State Bank of India", result.Value.BankName);
        Assert.Equal("123456789012", result.Value.AccountNumber);
        Assert.Equal("Savings", result.Value.AccountType);
        Assert.Equal("SBIN0001234", result.Value.IfscCode);
        Assert.Equal("Chandra Kumar", result.Value.AccountHolderName);

        await _bankAccountRepository.Received(1).AddAsync(Arg.Is<BankAccount>(ba =>
            ba.ShopId == shopId
            && ba.BankName == "State Bank of India"
            && ba.AccountNumber == "123456789012"
            && ba.AccountType == BankAccountType.Savings
            && ba.IfscCode == "SBIN0001234"
            && ba.AccountHolderName == "Chandra Kumar"), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithNullBankDetails_ReturnsValidationError()
    {
        var handler = new AddBankAccountCommandHandler(_bankAccountRepository, _unitOfWork);

        var result = await handler.HandleAsync(new AddBankAccountCommand(
            Guid.NewGuid(), null, null, null, null, null), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("BankAccount.Required", result.FirstError.Code);
        await _bankAccountRepository.DidNotReceive().AddAsync(Arg.Any<BankAccount>(), Arg.Any<CancellationToken>());
    }
}
