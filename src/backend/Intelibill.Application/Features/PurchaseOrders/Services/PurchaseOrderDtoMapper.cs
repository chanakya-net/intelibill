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
            po.SupplierName,
            po.SupplierReference,
            po.Lines.Count,
            po.Lines.Sum(line => line.ExpectedQuantity),
            po.Lines.Sum(line => line.ReceivedQuantity),
            po.ExpectedTotal,
            po.CreatedAt);

    public static PurchaseOrderDetailDto ToDetail(PurchaseOrder po) =>
        new(
            po.Id,
            po.PurchaseOrderNumber,
            po.Status,
            po.SupplierId,
            po.OrderDate,
            po.ExpectedDeliveryDate,
            po.SupplierReferenceNumber,
            po.Notes,
            po.Lines.Select(ToLineDto).ToList(),
            po.ExpectedTotal,
            po.CreatedAt,
            po.CancellationReason,
            po.SupplierName,
            po.SupplierReference,
            po.Lines.Sum(l => l.ReceivedQuantity),
            po.Receipts.OrderByDescending(r => r.ReceivedAt).Select(ToReceiptDto).ToList());

    private static PurchaseOrderLineDto ToLineDto(PurchaseOrderLine line) =>
        new(
            line.Id,
            line.ItemId,
            line.Description,
            line.ExpectedQuantity,
            line.ReceivedQuantity,
            line.RemainingQuantity,
            line.UnitCost,
            line.LineTotal);

    private static PurchaseOrderReceiptDto ToReceiptDto(PurchaseOrderReceipt receipt) =>
        new(
            receipt.Id,
            receipt.ReceiptNumber,
            receipt.ReceivedAt,
            receipt.ReferenceNumber,
            receipt.Notes,
            receipt.Lines.Select(line => new PurchaseOrderReceiptLineDto(
                line.Id,
                line.PurchaseOrderLineId,
                line.ItemId,
                line.InventoryBatchId,
                line.StockTransactionId,
                line.Quantity,
                line.TotalPurchaseCost,
                line.UnitCost)).ToList());
}
