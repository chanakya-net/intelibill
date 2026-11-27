using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Suppliers.Commands.DeleteSupplier;

public sealed class DeleteSupplierCommandHandler(
    IUserRepository userRepository,
    ISupplierRepository supplierRepository,
    IUnitOfWork unitOfWork,
    IShopLicenceFence licenceFence,
    ILiveSessionAuthority authority)
{
    [LicenceAction(LicenceAction.SourceCorrection)]
    public async Task<ErrorOr<Success>> HandleAsync(DeleteSupplierCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role != ShopRole.Owner)
            return Errors.Supplier.UserIsNotOwner;

        return await PostingTransaction.RunAsync<Success>(unitOfWork,
            ct => WriteAsync(command, ct), cancellationToken);
    }

    private async Task<ErrorOr<Success>> WriteAsync(DeleteSupplierCommand command, CancellationToken cancellationToken)
    {
        var lease = await licenceFence.EnterForSessionAsync(authority, command.Session, command.ActorUserId,
            command.ActiveShopId, LicenceAction.SourceCorrection, cancellationToken);
        if (lease.IsError) return lease.Errors;
        if (lease.Value.CurrentDecision().EnforcedDenial() is { } denial) return denial;

        var supplier = await supplierRepository.GetByIdAsync(command.SupplierId, cancellationToken);
        if (supplier is null || supplier.ShopId != command.ActiveShopId)
            return Errors.Supplier.SupplierNotFound;

        if (supplier.IsSystem)
            return Errors.Supplier.CannotModifySystemSupplier;

        if (lease.Value.CurrentDecision().ExistingSourceDenial(supplier.CreatedAt) is { } sourceDenial)
            return sourceDenial;

        supplierRepository.Remove(supplier);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
