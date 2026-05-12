using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.BankAccounts.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.BankAccounts.Commands.UpdateBankAccount;

public sealed class UpdateBankAccountCommandHandler(
    IBankAccountRepository bankAccountRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<BankAccountDto>> HandleAsync(UpdateBankAccountCommand command, CancellationToken cancellationToken)
    {
        var bankAccount = await bankAccountRepository.GetByIdAsync(command.Id, cancellationToken);
        if (bankAccount is null || bankAccount.ShopId != command.ShopId)
        {
            return Error.NotFound("BankAccount.NotFound", "Bank account not found.");
        }

        if (string.IsNullOrWhiteSpace(command.BankName) || string.IsNullOrWhiteSpace(command.AccountNumber))
        {
            return Error.Validation("BankAccount.Required", "Bank name and account number are required.");
        }

        BankAccountType? accountType = Enum.TryParse<BankAccountType>(command.AccountType, true, out var parsed)
            ? parsed
            : null;

        bankAccount.Update(
            command.BankName,
            command.AccountNumber,
            accountType,
            command.IfscCode,
            command.AccountHolderName);

        bankAccountRepository.Update(bankAccount);
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
