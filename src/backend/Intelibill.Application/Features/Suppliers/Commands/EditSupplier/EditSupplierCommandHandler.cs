using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Suppliers.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Suppliers.Commands.EditSupplier;

public sealed class EditSupplierCommandHandler(
    IUserRepository userRepository,
    ISupplierRepository supplierRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<SupplierDto>> HandleAsync(EditSupplierCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role != ShopRole.Owner)
            return Errors.Supplier.UserIsNotOwner;

        var supplier = await supplierRepository.GetByIdAsync(command.SupplierId, cancellationToken);
        if (supplier is null)
            return Errors.Supplier.SupplierNotFound;

        if (supplier.OwnerUserId != command.ActorUserId)
            return Errors.Supplier.SupplierNotFound;

        supplier.Update(
            command.Name,
            command.ContactPersonName,
            command.ContactPersonPhone,
            command.Address,
            command.City,
            command.State,
            command.Pin,
            command.IsActive,
            command.IsPreferred);

        supplierRepository.Update(supplier);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new SupplierDto(
            supplier.Id,
            supplier.Name,
            supplier.ContactPersonName,
            supplier.ContactPersonPhone,
            supplier.Address,
            supplier.City,
            supplier.State,
            supplier.Pin,
            supplier.IsActive,
            supplier.IsPreferred,
            0m);
    }
}