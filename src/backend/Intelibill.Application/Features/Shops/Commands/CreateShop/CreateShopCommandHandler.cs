using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
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
    IUnitOfWork unitOfWork)
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
        var membership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, isFirstShop);

        shop.AddMembership(membership);
        user.AddShopMembership(membership);

        await shopRepository.AddAsync(shop, cancellationToken);
        await supplierRepository.AddAsync(Supplier.CreateUnknownSystemSupplier(shop.Id), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The shop and its membership are committed before the scope moves, so a rescope that
        // cannot complete leaves a usable shop the caller can switch into rather than losing it.
        return await sessions.RescopeAsync(command.UserId, command.SessionId, shop.Id, cancellationToken);
    }
}
