using ErrorOr;
using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Extensions;
using Intelibill.Application.Features.Shops.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using System.Linq;

namespace Intelibill.Application.Features.Shops.Commands.AddShopBankAccount;

public sealed class AddShopBankAccountCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IUnitOfWork unitOfWork,
    IValidator<AddShopBankAccountCommand>? validator = null)
{
    public async Task<ErrorOr<ShopDetailsDto>> HandleAsync(AddShopBankAccountCommand command, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateCommandAsync(command, cancellationToken);
        if (validationResult is not null) return validationResult.Value.Errors;

        var user = await userRepository.GetByIdWithDetailsAsync(command.UserId, cancellationToken);
        if (user is null)
            return Errors.Shop.UserNotFound;

        var membership = user.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ShopId);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role != ShopRole.Owner)
            return Errors.Shop.UserIsNotOwner;

        var shop = await shopRepository.GetByIdWithBankAccountsAsync(command.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        if (!string.IsNullOrWhiteSpace(command.BankName) && !string.IsNullOrWhiteSpace(command.AccountNumber))
        {
            BankAccountType? accountType = Enum.TryParse<BankAccountType>(command.AccountType, true, out var parsed)
                ? parsed
                : null;

            var newAccount = BankAccount.Create(shop.Id, command.BankName, command.AccountNumber, accountType, command.IfscCode, command.AccountHolderName);
            shop.AddBankAccount(newAccount);
            await shopRepository.AddBankAccountAsync(newAccount, cancellationToken);
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
            shop.BankAccounts.Select(ba => new BankAccountDto(ba.Id, ba.BankName, ba.AccountNumber, ba.AccountType?.ToString(), ba.IfscCode, ba.AccountHolderName)).ToList());
    }
}
