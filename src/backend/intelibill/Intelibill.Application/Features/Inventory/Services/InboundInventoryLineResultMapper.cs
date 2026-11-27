using Intelibill.Application.Features.Inventory.DTOs;

namespace Intelibill.Application.Features.Inventory.Services;

public static class InboundInventoryLineResultMapper
{
    public static AddInventoryResultDto ToResultDto(this InboundInventoryLineResult result) =>
        new(
            result.Item.Id,
            result.Item.Name,
            result.Item.Barcode,
            result.Batch.Id,
            result.Batch.BatchNumber,
            result.Batch.Quantity,
            result.Inventory.Quantity,
            result.Batch.SupplierId,
            result.StockTransaction.Id,
            result.StockTransaction.PerformedAt);
}
