using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Application.Features.Shops.Commands.DeclareShopTaxProfile;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Shops.Commands.CreateShop;

public sealed class CreateShopCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISupplierRepository supplierRepository,
    IAuthSessionService sessions,
    ITenantWriteScope tenantWriteScope,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<ErrorOr<AccessScopeResult>> HandleAsync(CreateShopCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdWithDetailsAsync(command.UserId, cancellationToken);
        if (user is null)
            return Errors.Shop.UserNotFound;

        var isFirstShop = user.ShopMemberships.Count == 0;
        var shop = Shop.Create(
            command.Name,
            command.Address,
            command.City,
            command.State,
            command.Pincode,
            command.ContactPerson,
            command.MobileNumber,
            command.GstNumber);
        if (command.TaxProfile is not null)
        {
            var declared = DeclareShopTaxProfileCommandHandler.Declare(shop, command.TaxProfile, command.GstNumber, user.Id, timeProvider.GetUtcNow());
            if (declared.IsError)
                return declared.Errors;
        }

        var membership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, isFirstShop);

        shop.AddMembership(membership);
        user.AddShopMembership(membership);

        // The system supplier is a tenant row for a shop that is not the caller's
        // active one — the rescope that makes it active happens after the commit
        // below. One transaction holds the shop, its membership and that supplier,
        // and the scope it runs under reverts with it.
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await tenantWriteScope.EnterAsync(shop.Id, cancellationToken);

            await shopRepository.AddAsync(shop, cancellationToken);
            await supplierRepository.AddAsync(Supplier.CreateUnknownSystemSupplier(shop.Id), cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            await unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        // The shop and its membership are committed before the scope moves, so a rescope that
        // cannot complete leaves a usable shop the caller can switch into rather than losing it.
        return await sessions.RescopeAsync(command.UserId, command.SessionId, shop.Id, cancellationToken);
    }
}
