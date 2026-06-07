using Intelibill.Application.Features.PurchaseOrders.DTOs;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.PurchaseOrders.Services;

public static class PurchaseOrderDtoMapper
{
    public static PurchaseOrderListItemDto ToListItem(PurchaseOrder po) =>
        new(
            po.Id,
            po.PurchaseOrderNumber,
            po.Status,
            SupplierName: null,
            SupplierReference: null,
            po.Lines.Count,
            po.Lines.Sum(line => line.ExpectedQuantity),
            ReceivedQuantity: 0,
            po.ExpectedTotal,
            po.CreatedAt);

    public static PurchaseOrderDetailDto ToDetail(PurchaseOrder po) =>
        new(
            po.Id,
            po.PurchaseOrderNumber,
            po.Status,
            po.Notes,
            po.Lines.Select(ToLineDto).ToList(),
            po.ExpectedTotal,
            po.CreatedAt);

    private static PurchaseOrderLineDto ToLineDto(PurchaseOrderLine line) =>
        new(
            line.Id,
            line.Description,
            line.ExpectedQuantity,
            line.UnitCost,
            line.LineTotal);
}
