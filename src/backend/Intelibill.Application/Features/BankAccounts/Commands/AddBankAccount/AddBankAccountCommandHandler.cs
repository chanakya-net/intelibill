using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.BankAccounts.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.BankAccounts.Commands.AddBankAccount;

public sealed class AddBankAccountCommandHandler(
    IBankAccountRepository bankAccountRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<BankAccountDto>> HandleAsync(AddBankAccountCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.BankName) || string.IsNullOrWhiteSpace(command.AccountNumber))
        {
            return Error.Validation("BankAccount.Required", "Bank name and account number are required.");
        }

        BankAccountType? accountType = Enum.TryParse<BankAccountType>(command.AccountType, true, out var parsed)
            ? parsed
            : null;

        var bankAccount = BankAccount.Create(
            command.ShopId,
            command.BankName,
            command.AccountNumber,
            accountType,
            command.IfscCode,
            command.AccountHolderName);

        await bankAccountRepository.AddAsync(bankAccount, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new BankAccountDto(
            bankAccount.Id,
            bankAccount.BankName,
            bankAccount.AccountNumber,
            bankAccount.AccountType?.ToString(),
            bankAccount.IfscCode,
            bankAccount.AccountHolderName);
    }
}
