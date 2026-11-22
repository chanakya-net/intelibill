using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.DTOs;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Intelibill.Application.Features.PurchaseOrders.Commands.UpdatePurchaseOrderDraft;

public sealed class UpdatePurchaseOrderDraftCommandHandler(
    IItemRepository itemRepository,
    ISupplierRepository supplierRepository,
    IPurchaseOrderRepository purchaseOrderRepository,
    PurchaseOrderTrading trading)
{
    [LicenceAction(LicenceAction.NewTrading)]
    public Task<ErrorOr<PurchaseOrderDetailDto>> HandleAsync(
        UpdatePurchaseOrderDraftCommand command, CancellationToken cancellationToken) =>
        trading.RunAsync(command.ActorUserId, command.ActiveShopId, command.Session,
            Errors.PurchaseOrder.UserCannotCreatePurchaseOrder, ct => StageAsync(command, ct), cancellationToken);

    private async Task<ErrorOr<PurchaseOrder>> StageAsync(
        UpdatePurchaseOrderDraftCommand command, CancellationToken cancellationToken)
    {
        Supplier? supplier = null;
        if (command.SupplierId is Guid supplierId)
        {
            supplier = await supplierRepository.GetByIdAsync(supplierId, cancellationToken);
            if (supplier is null || supplier.ShopId != command.ActiveShopId)
                return Errors.Supplier.SupplierNotFound;
        }

        var po = await purchaseOrderRepository.GetByShopAndIdAsync(
            command.ActiveShopId,
            command.PurchaseOrderId,
            cancellationToken);

        if (po is null)
        {
            return Errors.PurchaseOrder.NotFound;
        }

        if (po.Status != PurchaseOrderStatus.Draft)
        {
            return Errors.PurchaseOrder.CannotUpdateNonDraft;
        }

        var lines = await PurchaseOrderDraftLines.ValidateAsync(command.ActiveShopId,
            command.Lines.Select(line => (line.ItemId, line.Description, line.ExpectedQuantity, line.UnitCost)),
            itemRepository, cancellationToken);
        if (lines.IsError) return lines.Errors;

        po.UpdateDraft(
            command.SupplierId,
            command.OrderDate,
            command.ExpectedDeliveryDate,
            command.SupplierReferenceNumber,
            command.Notes,
            lines.Value);

        po.UpdateSupplierDetails(supplier?.Name, supplier is null ? null : po.SupplierReference);
        purchaseOrderRepository.Update(po);
        return po;
    }
}
