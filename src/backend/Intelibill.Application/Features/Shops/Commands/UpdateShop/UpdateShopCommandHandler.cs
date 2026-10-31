using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Shops.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using System.Linq;

namespace Intelibill.Application.Features.Shops.Commands.UpdateShop;

public sealed class UpdateShopCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IBankAccountRepository bankAccountRepository,
    ITenantWriteScope tenantWriteScope,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<ShopDetailsDto>> HandleAsync(UpdateShopCommand command, CancellationToken cancellationToken)
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

        shop.UpdateDetails(
            command.Name,
            command.Address,
            command.City,
            command.State,
            command.Pincode,
            command.ContactPerson,
            command.MobileNumber,
            command.GstNumber);

        shopRepository.Update(shop);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The response carries the shop's bank details, and bank_accounts is tenant
        // data for a shop that need not be the caller's active one — the Owner
        // membership re-read above is what authorizes this. Without the scope the
        // read comes back empty and every bank field in the response is silently
        // null. Nothing is written here, so the transaction is rolled back.
        BankAccount? primaryAccount;
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await tenantWriteScope.EnterAsync(command.ShopId, cancellationToken);

            var bankAccounts = await bankAccountRepository.FindAsync(x => x.ShopId == command.ShopId, cancellationToken);
            primaryAccount = bankAccounts.Count > 0 ? bankAccounts[0] : null;
        }
        finally
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
        }

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
            primaryAccount?.BankName,
            primaryAccount?.AccountNumber,
            primaryAccount?.AccountType?.ToString(),
            primaryAccount?.IfscCode,
            primaryAccount?.AccountHolderName);
    }
}
