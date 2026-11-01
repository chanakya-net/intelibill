using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Shops.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using System.Linq;

namespace Intelibill.Application.Features.Shops.Queries.GetShopDetails;

public sealed class GetShopDetailsQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IBankAccountRepository bankAccountRepository,
    ITenantWriteScope tenantWriteScope,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<ShopDetailsDto>> HandleAsync(GetShopDetailsQuery query, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdWithDetailsAsync(query.UserId, cancellationToken);
        if (user is null)
            return Errors.Shop.UserNotFound;

        var membership = user.ShopMemberships.FirstOrDefault(sm => sm.ShopId == query.ShopId);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var shop = await shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        // bank_accounts is tenant data and the requested shop need not be the
        // caller's active one, so the read runs under a transaction-local scope
        // narrowed to the shop whose membership was just re-read above. Nothing is
        // written, so the transaction is rolled back.
        BankAccount? primaryAccount;
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await tenantWriteScope.EnterAsync(query.ShopId, cancellationToken);

            var bankAccounts = await bankAccountRepository.FindAsync(x => x.ShopId == query.ShopId, cancellationToken);
            primaryAccount = bankAccounts.Count > 0 ? bankAccounts[0] : null;
        }
        finally
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
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
