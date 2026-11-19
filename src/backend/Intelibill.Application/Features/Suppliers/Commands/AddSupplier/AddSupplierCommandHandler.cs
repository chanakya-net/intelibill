using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Suppliers.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Application.Common.Licensing;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Suppliers.Commands.AddSupplier;

public sealed class AddSupplierCommandHandler(
    IUserRepository userRepository,
    ISupplierRepository supplierRepository,
    DefinitionCreationTransaction transaction)
{
    [LicenceAction(LicenceAction.OperationalCapacity)]
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

        return await transaction.RunAsync<SupplierDto>(command.Session,
            (lease, ct) => CreateAsync(command, lease, ct), cancellationToken);
    }

    private async Task<ErrorOr<SupplierDto>> CreateAsync(AddSupplierCommand command, ShopLicenceLease lease, CancellationToken ct)
    {
        if (lease.Decision.EnforcedDenial() is { } denial)
            return denial;

        var supplier = Supplier.Create(
            command.ActiveShopId,
            command.Name,
            command.ContactPersonName,
            command.ContactPersonPhone,
            command.Address,
            command.City,
            command.State,
            command.Pin,
            command.IsActive,
            command.IsPreferred,
            isSystem: false);

        await supplierRepository.AddAsync(supplier, ct);

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
            supplier.IsSystem,
            supplier.IsActive,
            supplier.IsPreferred,
            0m);
}
