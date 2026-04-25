using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Commands.ReassignBatchSupplier;

public sealed class ReassignBatchSupplierCommandHandler(
    IUserRepository userRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository,
    ISupplierRepository supplierRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<Success>> HandleAsync(ReassignBatchSupplierCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role != ShopRole.Owner)
            return Errors.Supplier.UserIsNotOwner;

        var batch = await inventoryBatchRepository.GetByIdAsync(command.BatchId, cancellationToken);
        if (batch is null || batch.ShopId != command.ActiveShopId)
            return Errors.Inventory.BatchNotFound;

        var goodsReceivedEntry = (await supplierLedgerEntryRepository.GetByBatchAsync(command.ActiveShopId, batch.Id, cancellationToken))
            .FirstOrDefault(e => e.EntryType == SupplierLedgerEntryType.GoodsReceived);

        if (goodsReceivedEntry is null)
            return Errors.Inventory.SupplierLedgerEntryInvalid;

        var currentSupplier = await supplierRepository.GetByIdAsync(goodsReceivedEntry.SupplierId, cancellationToken);
        if (currentSupplier is null || currentSupplier.ShopId != command.ActiveShopId)
            return Errors.Supplier.SupplierNotFound;

        if (!currentSupplier.IsSystem)
            return Errors.Supplier.CannotReassignFromRealSupplier;

        var newSupplier = await supplierRepository.GetByIdAsync(command.NewSupplierId, cancellationToken);
        if (newSupplier is null || newSupplier.ShopId != command.ActiveShopId)
            return Errors.Supplier.SupplierNotFound;

        if (newSupplier.IsSystem)
            return Errors.Supplier.CannotModifySystemSupplier;

        goodsReceivedEntry.ReassignSupplier(newSupplier.Id);
        batch.AssignSupplier(newSupplier.Id, command.ActorUserId);

        supplierLedgerEntryRepository.Update(goodsReceivedEntry);
        inventoryBatchRepository.Update(batch);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success;
    }
}
