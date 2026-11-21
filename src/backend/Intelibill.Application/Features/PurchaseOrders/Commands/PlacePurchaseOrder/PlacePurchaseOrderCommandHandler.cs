using Intelibill.Domain.Entities;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.DTOs;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Intelibill.Application.Features.PurchaseOrders.Commands.PlacePurchaseOrder;

public sealed class PlacePurchaseOrderCommandHandler(
    ISupplierRepository supplierRepository,
    IPurchaseOrderRepository purchaseOrderRepository,
    PurchaseOrderTrading trading)
{
    [LicenceAction(LicenceAction.NewTrading)]
    public Task<ErrorOr<PurchaseOrderDetailDto>> HandleAsync(
        PlacePurchaseOrderCommand command, CancellationToken cancellationToken) =>
        trading.RunAsync(command.ActorUserId, command.ActiveShopId, command.Session,
            Errors.PurchaseOrder.UserCannotMutatePurchaseOrder, ct => StageAsync(command, ct), cancellationToken);

    private async Task<ErrorOr<PurchaseOrder>> StageAsync(
        PlacePurchaseOrderCommand command, CancellationToken cancellationToken)
    {
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

        po.UpdateSupplierDetails(supplier.Name, po.SupplierReference);
        po.Place(po.SupplierId.Value);

        purchaseOrderRepository.Update(po);
        return po;
    }
}
