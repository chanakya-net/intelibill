using ErrorOr;
using Intelibill.Application.Features.BankAccounts.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.BankAccounts.Queries.GetBankAccounts;

public sealed class GetBankAccountsQueryHandler(
    IBankAccountRepository bankAccountRepository)
{
    public async Task<ErrorOr<IReadOnlyList<BankAccountDto>>> HandleAsync(GetBankAccountsQuery query, CancellationToken cancellationToken)
    {
        var bankAccounts = await bankAccountRepository.GetByOwnerUserIdAsync(query.OwnerUserId, cancellationToken);

        return bankAccounts.Select(ba => new BankAccountDto(
            ba.Id,
            ba.BankName,
            ba.AccountNumber,
            ba.AccountType?.ToString(),
            ba.IfscCode,
            ba.AccountHolderName)).ToList();
    }
}
