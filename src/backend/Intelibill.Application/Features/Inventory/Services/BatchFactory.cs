using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.AddInventoryBatch;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Services;

internal sealed class BatchFactory(
    IInventoryBatchRepository inventoryBatchRepository,
    IStockTransactionRepository stockTransactionRepository,
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository) : IBatchFactory
{
    public async Task<ErrorOr<BatchCreationResult>> CreateBatchAsync(
        Guid shopId,
        Guid itemId,
        AddInventoryBatchRowCommand row,
        Supplier supplier,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var normalizedBatchNumber = row.BatchNumber.Trim();

        var existingBatch = await inventoryBatchRepository.GetByBatchNumberAsync(
            shopId, itemId, normalizedBatchNumber, cancellationToken);

        if (existingBatch is not null && !existingBatch.IsVoided)
            return Errors.Inventory.BatchNumberAlreadyExists;

        var isSystemSupplier = row.SupplierId is null;

        var batchResult = InventoryBatch.Create(
            shopId,
            itemId,
            normalizedBatchNumber,
            row.Quantity,
            row.CostPrice,
            row.Mrp,
            row.SalesPrice,
            row.TaxRatePercent,
            row.TaxIncluded,
            row.ExpiryDate,
            row.ManufacturingDate,
            supplier.Id,
            actorUserId);

        if (batchResult.IsError)
            return batchResult.Errors;

        var batch = batchResult.Value;
        await inventoryBatchRepository.AddAsync(batch, cancellationToken);

        var performedAt = (row.PerformedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();

        var stockTransactionResult = StockTransaction.Create(
            shopId,
            itemId,
            batch.Id,
            StockTransactionType.In,
            row.Quantity,
            row.ReferenceNumber,
            row.Notes,
            performedAt,
            actorUserId,
            actorUserId);

        if (stockTransactionResult.IsError)
            return stockTransactionResult.Errors;

        var stockTransaction = stockTransactionResult.Value;
        await stockTransactionRepository.AddAsync(stockTransaction, cancellationToken);

        var ledgerAmount = decimal.Round(row.CostPrice * row.Quantity, 2, MidpointRounding.AwayFromZero);

        var ledgerResult = SupplierLedgerEntry.Create(
            shopId,
            supplier.Id,
            batch.Id,
            SupplierLedgerEntryType.GoodsReceived,
            ledgerAmount,
            DateOnly.FromDateTime(performedAt.UtcDateTime),
            isSystemSupplier ? "Receipt with no supplier assigned" : null,
            actorUserId);

        if (ledgerResult.IsError)
            return Errors.Inventory.SupplierLedgerEntryInvalid;

        var ledgerEntry = ledgerResult.Value;
        await supplierLedgerEntryRepository.AddAsync(ledgerEntry, cancellationToken);

        return new BatchCreationResult(batch, stockTransaction, ledgerEntry);
    }
}
