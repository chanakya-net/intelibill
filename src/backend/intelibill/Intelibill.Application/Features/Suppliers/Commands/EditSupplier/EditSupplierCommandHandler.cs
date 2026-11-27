using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Suppliers.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.SupplierReturns.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Suppliers.Commands.EditSupplier;

public sealed class EditSupplierCommandHandler(
    IUserRepository userRepository,
    ISupplierRepository supplierRepository,
    DefinitionCreationTransaction transaction,
    ISupplierReturnMutationLock supplierLock,
    ILiveSessionAuthority authority)
{
    [LicenceAction(LicenceAction.OperationalCapacity)]
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

        return await transaction.RunAsync<SupplierDto>(command.Session,
            (lease, ct) => EditAsync(command, lease, ct), cancellationToken);
    }

    private async Task<ErrorOr<SupplierDto>> EditAsync(EditSupplierCommand command, ShopLicenceLease lease, CancellationToken ct)
    {
        var denial = lease.Decision.EnforcedDenial();
        if (lease.SuspensionDenial() is { } suspension)
            return suspension;
        if (denial?.Code == Errors.Licence.TradingRestricted.Code)
            return denial.Value;

        var requiresActiveSource = denial is not null && command.IsActive;
        var source = await LoadSourceAsync(command, requiresActiveSource, ct);
        if (source.IsError)
            return source.Errors;

        var supplier = source.Value;
        if (supplier.IsSystem)
            return Errors.Supplier.CannotModifySystemSupplier;
        if (requiresActiveSource && !supplier.IsActive)
            return denial!.Value;

        return Update(supplier, command);
    }

    private async Task<ErrorOr<Supplier>> LoadSourceAsync(EditSupplierCommand command, bool lockSource, CancellationToken ct)
    {
        Supplier? supplier;
        if (lockSource)
        {
            // A restricted edit may keep an active supplier active. Serialize against deactivation.
            var outcome = await supplierLock.LockAsync(command.ActiveShopId, command.SupplierId, [], ct);
            if (outcome == RowLockOutcome.TimedOut)
                return Errors.General.Conflict("The supplier is being updated. Try again.");
            if (!await authority.IsValidAsync(command.Session, ct))
                return Errors.Auth.SessionInvalidated;
            if (outcome == RowLockOutcome.NotFound)
                return Errors.Supplier.SupplierNotFound;

            supplier = await supplierRepository.GetCurrentForEditAsync(command.ActiveShopId, command.SupplierId, ct);
        }
        else
        {
            supplier = await supplierRepository.GetByIdAsync(command.SupplierId, ct);
        }

        if (supplier is null || supplier.ShopId != command.ActiveShopId)
            return Errors.Supplier.SupplierNotFound;
        return supplier;
    }

    private SupplierDto Update(Supplier supplier, EditSupplierCommand command)
    {
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

        return new SupplierDto(
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
}
