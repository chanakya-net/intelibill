using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Shops.Setup;
using ErrorOr;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.BankAccounts.Commands.DeleteBankAccount;

public sealed class DeleteBankAccountCommandHandler(
    IBankAccountRepository bankAccountRepository,
    ShopSetupAccess access)
{
    [LicenceAction(LicenceAction.OperationalCapacity)]
    public Task<ErrorOr<Deleted>> HandleAsync(DeleteBankAccountCommand command, CancellationToken ct)
    {
        if (command.Session is not { ActiveShopId: { } active } session || active != command.ShopId)
            return Task.FromResult<ErrorOr<Deleted>>(Errors.Auth.SessionInvalidated);

        return access.WriteAsync<Deleted>(session, session.UserId, command.ShopId, LicenceAction.OperationalCapacity,
            (_, lease, token) => WriteAsync(command, lease, token), ct);
    }

    private async Task<ErrorOr<Deleted>> WriteAsync(DeleteBankAccountCommand command, ShopLicenceLease lease, CancellationToken cancellationToken)
    {
        var bankAccount = await bankAccountRepository.GetByIdAsync(command.Id, cancellationToken);
        if (bankAccount is null || bankAccount.ShopId != command.ShopId)
        {
            return Errors.BankAccount.NotFound;
        }

        if (ShopSetupAccess.Denial(lease.CurrentDecision()) is { } denied) return denied;

        bankAccountRepository.Remove(bankAccount);

        return Result.Deleted;
    }
}
