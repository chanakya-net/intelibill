using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.DTOs;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Intelibill.Application.Features.PurchaseOrders.Commands.CancelPurchaseOrder;

public sealed class CancelPurchaseOrderCommandHandler(
    IUserRepository userRepository,
    IPurchaseOrderRepository purchaseOrderRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<PurchaseOrderDetailDto>> HandleAsync(
        CancelPurchaseOrderCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var membership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role != ShopRole.Owner && membership.Role != ShopRole.Manager)
            return Errors.PurchaseOrder.UserCannotMutatePurchaseOrder;

        var po = await purchaseOrderRepository.GetByShopAndIdAsync(
            command.ActiveShopId,
            command.PurchaseOrderId,
            cancellationToken);

        if (po is null)
            return Errors.PurchaseOrder.NotFound;

        if (po.Status != PurchaseOrderStatus.Placed)
            return Errors.PurchaseOrder.CannotCancelInvalidStatus;

        if (string.IsNullOrWhiteSpace(command.Reason))
            return Errors.PurchaseOrder.CancellationReasonRequired;

        if (po.Lines.Sum(l => l.ReceivedQuantity) > 0)
            return Errors.PurchaseOrder.CannotCancelAfterReceipt;

        po.Cancel(command.Reason);

        purchaseOrderRepository.Update(po);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PurchaseOrderDtoMapper.ToDetail(po);
    }
}
