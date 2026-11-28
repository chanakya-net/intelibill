using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Shops.Setup;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.BankAccounts.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.BankAccounts.Commands.UpdateBankAccount;

public sealed class UpdateBankAccountCommandHandler(
    IBankAccountRepository bankAccountRepository,
    ShopSetupAccess access)
{
    [LicenceAction(LicenceAction.OperationalCapacity)]
    public Task<ErrorOr<BankAccountDto>> HandleAsync(UpdateBankAccountCommand command, CancellationToken ct)
    {
        if (command.Session is not { ActiveShopId: { } active } session || active != command.ShopId)
            return Task.FromResult<ErrorOr<BankAccountDto>>(Errors.Auth.SessionInvalidated);

        return access.WriteAsync<BankAccountDto>(session, session.UserId, command.ShopId, LicenceAction.OperationalCapacity,
            (_, lease, token) => WriteAsync(command, lease, token), ct);
    }

    private async Task<ErrorOr<BankAccountDto>> WriteAsync(UpdateBankAccountCommand command, ShopLicenceLease lease, CancellationToken cancellationToken)
    {
        var bankAccount = await bankAccountRepository.GetByIdAsync(command.Id, cancellationToken);
        if (bankAccount is null || bankAccount.ShopId != command.ShopId)
        {
            return Errors.BankAccount.NotFound;
        }

        if (string.IsNullOrWhiteSpace(command.BankName) || string.IsNullOrWhiteSpace(command.AccountNumber))
        {
            return Errors.BankAccount.Required;
        }

        BankAccountType? accountType = Enum.TryParse<BankAccountType>(command.AccountType, true, out var parsed)
            ? parsed
            : null;

        if (ShopSetupAccess.Denial(lease.CurrentDecision()) is { } denied) return denied;

        bankAccount.Update(
            command.BankName,
            command.AccountNumber,
            accountType,
            command.IfscCode,
            command.AccountHolderName);

        bankAccountRepository.Update(bankAccount);

        return new BankAccountDto(
            bankAccount.Id,
            bankAccount.BankName,
            bankAccount.AccountNumber,
            bankAccount.AccountType?.ToString(),
            bankAccount.IfscCode,
            bankAccount.AccountHolderName);
    }
}
