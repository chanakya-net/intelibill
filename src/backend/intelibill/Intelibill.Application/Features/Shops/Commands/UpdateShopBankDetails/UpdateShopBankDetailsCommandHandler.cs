using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Shops.DTOs;
using Intelibill.Application.Features.Shops.Setup;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using System.Text.RegularExpressions;

namespace Intelibill.Application.Features.Shops.Commands.UpdateShopBankDetails;

public sealed class UpdateShopBankDetailsCommandHandler(ShopSetupAccess access, IBankAccountRepository banks)
{
    private static readonly Regex IfscRegex = new(
        "^[A-Z]{4}0[A-Z0-9]{6}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    [LicenceAction(LicenceAction.OperationalCapacity)]
    public Task<ErrorOr<ShopDetailsDto>> HandleAsync(UpdateShopBankDetailsCommand command, CancellationToken ct) =>
        access.WriteAsync<ShopDetailsDto>(command.Session, command.UserId, command.ShopId, LicenceAction.OperationalCapacity,
            (shop, lease, token) => WriteAsync(command, shop, lease, token), ct);

    private async Task<ErrorOr<ShopDetailsDto>> WriteAsync(UpdateShopBankDetailsCommand command, Shop shop,
        ShopLicenceLease lease, CancellationToken ct)
    {
        var bankName = ToOptional(command.BankName);
        var accountNumber = ToOptional(command.AccountNumber);
        if (bankName is null || accountNumber is null)
            return Errors.BankAccount.Required;

        var ifscCode = ToOptional(command.IfscCode);
        if (ifscCode is not null && !IfscRegex.IsMatch(ifscCode))
            return Errors.Shop.IfscCodeInvalid;

        BankAccountType? accountType = null;
        var requestedAccountType = ToOptional(command.AccountType);
        if (requestedAccountType is not null)
        {
            if (!Enum.TryParse<BankAccountType>(requestedAccountType, true, out var parsedAccountType))
                return Errors.Shop.BankAccountTypeInvalid;

            accountType = parsedAccountType;
        }

        var accountHolderName = ToOptional(command.AccountHolderName);

        var accounts = await banks.FindAsync(x => x.ShopId == shop.Id, ct);
        var decision = lease.CurrentDecision();
        if (ShopSetupAccess.Denial(decision) is { } denied) return denied;

        var account = accounts.Count > 0 ? accounts[0] : null;
        if (account is null)
        {
            account = BankAccount.Create(shop.Id, bankName, accountNumber, accountType, ifscCode, accountHolderName);
            await banks.AddAsync(account, ct);
        }
        else
        {
            account.Update(bankName, accountNumber, accountType, ifscCode, accountHolderName);
            banks.Update(account);
        }

        return ShopDetailsDto.From(shop, account, ShopSetupAccess.Hint(shop.Id, ShopRole.Owner, decision));
    }

    private static string? ToOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
