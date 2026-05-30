using ErrorOr;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.BankAccounts.Commands.DeleteBankAccount;

public sealed class DeleteBankAccountCommandHandler(
    IBankAccountRepository bankAccountRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<Deleted>> HandleAsync(DeleteBankAccountCommand command, CancellationToken cancellationToken)
    {
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
