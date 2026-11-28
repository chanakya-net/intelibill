using ErrorOr;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Services;

/// <summary>Takes a sold quantity out of a batch and its inventory and adds the sale's OUT stock movement.</summary>
public sealed class SaleStockOutPoster(IStockTransactionRepository stockTransactionRepository)
{
    /// <summary>
    /// Subtracts <paramref name="quantity"/> from the batch, then from the inventory, then adds the OUT movement;
    /// the first refused step's errors are returned and later steps are not run. The caller owns any stock checks.
    /// </summary>
    internal async Task<ErrorOr<Success>> PostStockOutAsync(
        Guid shopId,
        Guid itemId,
        InventoryBatch batch,
        Domain.Entities.Inventory inventory,
        decimal quantity,
        string invoiceNumber,
        DateTimeOffset soldAt,
        Guid performedByUserId,
        Guid recordedByUserId,
        CancellationToken cancellationToken)
    {
        var batchResult = batch.SubtractQuantity(quantity, recordedByUserId);
        if (batchResult.IsError) return batchResult.Errors;

        var inventoryResult = inventory.SubtractQuantity(quantity, recordedByUserId);
        if (inventoryResult.IsError) return inventoryResult.Errors;

        var txResult = StockTransaction.Create(
            shopId,
            itemId,
            batch.Id,
            StockTransactionType.Out,
            -quantity,
            invoiceNumber,
            null,
            soldAt,
            performedByUserId,
            recordedByUserId);
        if (txResult.IsError) return txResult.Errors;
        await stockTransactionRepository.AddAsync(txResult.Value, cancellationToken);

        return Result.Success;
    }
}
