using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.DTOs;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.PurchaseOrders.Commands.ClosePurchaseOrder;

public sealed class ClosePurchaseOrderCommandHandler(
    IUserRepository userRepository,
    IPurchaseOrderRepository purchaseOrderRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<PurchaseOrderDetailDto>> HandleAsync(
        ClosePurchaseOrderCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var membership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.PurchaseOrder.UserCannotMutatePurchaseOrder;

        if (string.IsNullOrWhiteSpace(command.Reason))
            return Errors.PurchaseOrder.CloseReasonRequired;

        var po = await purchaseOrderRepository.GetByShopAndIdAsync(
            command.ActiveShopId,
            command.PurchaseOrderId,
            cancellationToken);

        if (po is null)
            return Errors.PurchaseOrder.NotFound;

        if (po.Status != PurchaseOrderStatus.PartiallyReceived)
            return Errors.PurchaseOrder.CannotCloseInvalidStatus;

        po.Close(command.ActorUserId, command.Reason, DateTimeOffset.UtcNow);
        purchaseOrderRepository.Update(po);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PurchaseOrderDtoMapper.ToDetail(po);
    }
}
