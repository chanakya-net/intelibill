using Intelibill.Application.Features.BankAccounts.Queries.GetBankAccounts;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.BankAccounts.Queries.GetBankAccounts;

public class GetBankAccountsQueryHandlerTests
{
    private readonly IBankAccountRepository _bankAccountRepository = Substitute.For<IBankAccountRepository>();
    private readonly GetBankAccountsQueryHandler _handler;

    public GetBankAccountsQueryHandlerTests()
    {
        _handler = new GetBankAccountsQueryHandler(_bankAccountRepository);
    }

    [Fact]
    public async Task HandleAsync_WhenNoAccounts_ReturnsEmptyList()
    {
        var ownerId = Guid.NewGuid();
        _bankAccountRepository.GetByOwnerUserIdAsync(ownerId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BankAccount>());

        var result = await _handler.HandleAsync(new GetBankAccountsQuery(ownerId), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task HandleAsync_WhenAccountsExist_ReturnsMappedDtos()
    {
        var ownerId = Guid.NewGuid();
        var a1 = BankAccount.Create(ownerId, "SBI", "111", BankAccountType.Savings, "SBIN0000001", "Alice");
        var a2 = BankAccount.Create(ownerId, "HDFC", "222", BankAccountType.Current, "HDFC0000001", null);
        _bankAccountRepository.GetByOwnerUserIdAsync(ownerId, Arg.Any<CancellationToken>())
            .Returns(new[] { a1, a2 });

        var result = await _handler.HandleAsync(new GetBankAccountsQuery(ownerId), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);
        Assert.Contains(result.Value, d => d.BankName == "SBI" && d.AccountType == "Savings");
        Assert.Contains(result.Value, d => d.BankName == "HDFC" && d.AccountHolderName == null);
    }
}
