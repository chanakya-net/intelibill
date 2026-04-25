using ErrorOr;
using FluentValidation;
using Intelibill.Application.Common.Extensions;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.BankAccounts.Commands.DeleteBankAccount;

public sealed class DeleteBankAccountCommandHandler(
    IBankAccountRepository bankAccountRepository,
    IUnitOfWork unitOfWork,
    IValidator<DeleteBankAccountCommand>? validator = null)
{
    public async Task<ErrorOr<Deleted>> HandleAsync(DeleteBankAccountCommand command, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateCommandAsync(command, cancellationToken);
        if (validationResult is not null) return validationResult.Value.Errors;

        var bankAccount = await bankAccountRepository.GetByIdAsync(command.Id, cancellationToken);
        if (bankAccount is null || bankAccount.ShopId != command.ShopId)
        {
            return Error.NotFound("BankAccount.NotFound", "Bank account not found.");
        }

        bankAccountRepository.Remove(bankAccount);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Deleted;
    }
}
