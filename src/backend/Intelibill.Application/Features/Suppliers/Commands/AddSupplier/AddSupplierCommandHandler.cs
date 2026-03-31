using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Suppliers.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Suppliers.Commands.AddSupplier;

public sealed class AddSupplierCommandHandler(
    IUserRepository userRepository,
    ISupplierRepository supplierRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<SupplierDto>> HandleAsync(AddSupplierCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role != ShopRole.Owner)
            return Errors.Supplier.UserIsNotOwner;

        var supplier = Supplier.Create(
            command.ActorUserId,
            command.Name,
            command.ContactPersonName,
            command.ContactPersonPhone,
            command.Address,
            command.City,
            command.State,
            command.Pin,
            command.IsActive,
            command.IsPreferred);

        await supplierRepository.AddAsync(supplier, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(supplier);
    }

    private static SupplierDto ToDto(Supplier supplier) =>
        new(
            supplier.Id,
            supplier.Name,
            supplier.ContactPersonName,
            supplier.ContactPersonPhone,
            supplier.Address,
            supplier.City,
            supplier.State,
            supplier.Pin,
            supplier.IsActive,
            supplier.IsPreferred);
}