using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.DTOs;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Intelibill.Application.Features.PurchaseOrders.Commands.UpdatePurchaseOrderDraft;

public sealed class UpdatePurchaseOrderDraftCommandHandler(
    IUserRepository userRepository,
    IPurchaseOrderRepository purchaseOrderRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<PurchaseOrderDetailDto>> HandleAsync(
        UpdatePurchaseOrderDraftCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var membership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role != ShopRole.Owner && membership.Role != ShopRole.Manager)
            return Errors.PurchaseOrder.UserCannotCreatePurchaseOrder;

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var po = await purchaseOrderRepository.GetByShopAndIdAsync(
                command.ActiveShopId,
                command.PurchaseOrderId,
                cancellationToken);

            if (po is null)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Errors.PurchaseOrder.NotFound;
            }

            if (po.Status != PurchaseOrderStatus.Draft)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Errors.PurchaseOrder.CannotUpdateNonDraft;
            }

            var itemIds = new HashSet<Guid>();
            var linesToUpdate = new List<(Guid ItemId, string Description, int ExpectedQuantity, decimal UnitCost)>();

            foreach (var lineInput in command.Lines)
            {
                if (lineInput.ItemId == Guid.Empty)
                {
                    await unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return Errors.PurchaseOrder.LineDescriptionRequired;
                }

                var description = lineInput.Description?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(description))
                {
                    await unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return Errors.PurchaseOrder.LineDescriptionRequired;
                }

                if (!itemIds.Add(lineInput.ItemId))
                {
                    await unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return Errors.PurchaseOrder.DuplicateItem;
                }

                if (lineInput.ExpectedQuantity <= 0)
                {
                    await unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return Errors.PurchaseOrder.InvalidLineQuantity;
                }

                if (lineInput.UnitCost < 0)
                {
                    await unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return Errors.PurchaseOrder.InvalidLineUnitCost;
                }

                linesToUpdate.Add((lineInput.ItemId, description, lineInput.ExpectedQuantity, lineInput.UnitCost));
            }

            po.UpdateDraft(
                command.SupplierId,
                command.OrderDate,
                command.ExpectedDeliveryDate,
                command.SupplierReferenceNumber,
                command.Notes,
                linesToUpdate);

            purchaseOrderRepository.Update(po);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);

            return PurchaseOrderDtoMapper.ToDetail(po);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
