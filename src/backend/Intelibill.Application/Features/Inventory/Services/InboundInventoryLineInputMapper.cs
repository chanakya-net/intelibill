using Intelibill.Application.Features.Inventory.Commands.AddInventoryBatch;

namespace Intelibill.Application.Features.Inventory.Services;

public static class InboundInventoryLineInputMapper
{
    public static InboundInventoryLineInput ToInboundInput(this AddInventoryBatchRowCommand row) =>
        new(
            null,
            row.ItemName,
            row.Barcode,
            row.ItemDescription,
            row.Uom,
            row.BatchNumber,
            row.Quantity,
            row.TotalPurchaseCost,
            row.Mrp,
            row.SalesPrice,
            row.TaxRatePercent,
            row.TaxIncluded,
            row.PurchaseTaxIncluded,
            row.ExpiryDate,
            row.ManufacturingDate,
            row.SupplierId,
            row.ReferenceNumber,
            row.Notes,
            row.PerformedAt,
            row.HsnCode);
}
