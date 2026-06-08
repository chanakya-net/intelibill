using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Shops.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using System.Linq;
using System.Text.RegularExpressions;

namespace Intelibill.Application.Features.Shops.Commands.UpdateShopBankDetails;

public sealed class UpdateShopBankDetailsCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IBankAccountRepository bankAccountRepository,
    IUnitOfWork unitOfWork)
{
    private static readonly Regex IfscRegex = new(
        "^[A-Z]{4}0[A-Z0-9]{6}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    public async Task<ErrorOr<ShopDetailsDto>> HandleAsync(UpdateShopBankDetailsCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdWithDetailsAsync(command.UserId, cancellationToken);
        if (user is null)
            return Errors.Shop.UserNotFound;

        var membership = user.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ShopId);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role != ShopRole.Owner)
            return Errors.Shop.UserIsNotOwner;

        var shop = await shopRepository.GetByIdAsync(command.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var bankName = ToOptional(command.BankName);
        var accountNumber = ToOptional(command.AccountNumber);
        if (bankName is null || accountNumber is null)
            return Error.Validation("BankAccount.Required", "Bank name and account number are required.");

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
        var bankAccounts = await bankAccountRepository.FindAsync(x => x.ShopId == command.ShopId, cancellationToken);
        var primaryAccount = bankAccounts.Count > 0 ? bankAccounts[0] : null;

        if (primaryAccount is null)
        {
            primaryAccount = BankAccount.Create(
                shop.Id,
                bankName,
                accountNumber,
                accountType,
                ifscCode,
                accountHolderName);
            await bankAccountRepository.AddAsync(primaryAccount, cancellationToken);
        }
        else
        {
            primaryAccount.Update(
                bankName,
                accountNumber,
                accountType,
                ifscCode,
                accountHolderName);
            bankAccountRepository.Update(primaryAccount);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ShopDetailsDto(
            shop.Id,
            shop.Name,
            shop.Address,
            shop.City,
            shop.State,
            shop.Pincode,
            shop.ContactPerson,
            shop.MobileNumber,
            shop.GstNumber,
            primaryAccount.BankName,
            primaryAccount.AccountNumber,
            primaryAccount.AccountType?.ToString(),
            primaryAccount.IfscCode,
            primaryAccount.AccountHolderName);
    }

    private static string? ToOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
