using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.DTOs;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Intelibill.Application.Features.PurchaseOrders.Commands.PlacePurchaseOrder;

public sealed class PlacePurchaseOrderCommandHandler(
    IUserRepository userRepository,
    ISupplierRepository supplierRepository,
    IPurchaseOrderRepository purchaseOrderRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<PurchaseOrderDetailDto>> HandleAsync(
        PlacePurchaseOrderCommand command,
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

        if (po.Status != PurchaseOrderStatus.Draft)
            return Errors.PurchaseOrder.CannotPlaceNonDraft;

        if (po.SupplierId is null)
            return Errors.PurchaseOrder.SupplierRequired;

        var supplier = await supplierRepository.GetByIdAsync(po.SupplierId.Value, cancellationToken);
        if (supplier is null || supplier.ShopId != command.ActiveShopId || !supplier.IsActive || supplier.IsSystem)
            return Errors.PurchaseOrder.SupplierInvalidForPlacement;

        if (po.Lines.Count == 0)
            return Errors.PurchaseOrder.AtLeastOneLineRequired;

        po.Place(po.SupplierId.Value);

        purchaseOrderRepository.Update(po);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PurchaseOrderDtoMapper.ToDetail(po);
    }
}
