using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Commands.AddInventory;

/// <summary>Stages a receipt's batch, its In movement, its posted value and the supplier payable it raises.</summary>
public sealed class ReceiptStockAndPayablePoster(
    IInventoryBatchRepository inventoryBatchRepository,
    IStockTransactionRepository stockTransactionRepository,
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository,
    IPurchaseReceiptValueRepository purchaseReceiptValueRepository,
    IBusinessClock businessClock)
{
    internal async Task<ErrorOr<InventoryBatch>> StageBatchAsync(
        AddInventoryCommand command,
        Item item,
        Supplier effectiveSupplier,
        CancellationToken cancellationToken)
    {
        var batchResult = InventoryBatch.Create(
            command.ActiveShopId,
            item.Id,
            command.BatchNumber,
            command.Quantity,
            ComputeUnitCost(command.TotalPurchaseCost, command.Quantity),
            command.Mrp,
            command.SalesPrice,
            command.TaxRatePercent,
            command.TaxIncluded,
            command.ExpiryDate,
            command.ManufacturingDate,
            effectiveSupplier.Id,
            command.ActorUserId,
            command.PurchaseTaxIncluded);

        if (batchResult.IsError)
            return batchResult.Errors;

        var batch = batchResult.Value;
        await inventoryBatchRepository.AddAsync(batch, cancellationToken);

        return batch;
    }

    internal async Task<ErrorOr<StockTransaction>> StageStockTransactionAsync(
        AddInventoryCommand command,
        Item item,
        InventoryBatch batch,
        DateTimeOffset performedAt,
        CancellationToken cancellationToken)
    {
        var stockTransactionResult = StockTransaction.Create(
            command.ActiveShopId,
            item.Id,
            batch.Id,
            StockTransactionType.In,
            command.Quantity,
            command.ReferenceNumber,
            command.Notes,
            performedAt,
            command.ActorUserId,
            command.ActorUserId);

        if (stockTransactionResult.IsError)
            return stockTransactionResult.Errors;

        var stockTransaction = stockTransactionResult.Value;
        await stockTransactionRepository.AddAsync(stockTransaction, cancellationToken);

        return stockTransaction;
    }

    internal async Task<ErrorOr<Success>> StageReceiptValueAsync(
        AddInventoryCommand command,
        Item item,
        InventoryBatch batch,
        Supplier effectiveSupplier,
        decimal ledgerAmount,
        DateTimeOffset performedAt,
        CancellationToken cancellationToken)
    {
        // The receipt's total as posted, kept so a later correction or void works from it rather
        // than from the rounded unit cost.
        var receiptValueResult = PurchaseReceiptValue.Create(
            command.ActiveShopId,
            batch.Id,
            item.Id,
            effectiveSupplier.Id,
            command.Quantity,
            ledgerAmount,
            command.ReferenceNumber,
            performedAt,
            command.ActorUserId);

        if (receiptValueResult.IsError)
            return receiptValueResult.Errors;

        await purchaseReceiptValueRepository.AddAsync(receiptValueResult.Value, cancellationToken);

        return Result.Success;
    }

    internal async Task<ErrorOr<Success>> StageSupplierLedgerEntryAsync(
        AddInventoryCommand command,
        Shop shop,
        InventoryBatch batch,
        Supplier effectiveSupplier,
        decimal ledgerAmount,
        DateTimeOffset performedAt,
        CancellationToken cancellationToken)
    {
        var isSystemSupplier = command.SupplierId is null;

        // A free receipt (sample, bonus stock) owes the supplier nothing: no zero-value payable.
        if (ledgerAmount != 0)
        {
            var ledgerResult = SupplierLedgerEntry.Create(
                command.ActiveShopId,
                effectiveSupplier.Id,
                batch.Id,
                SupplierLedgerEntryType.GoodsReceived,
                ledgerAmount,
                businessClock.GetBusinessDate(performedAt, shop.BusinessTimeZoneId),
                isSystemSupplier ? "Receipt with no supplier assigned" : null,
                command.ActorUserId);

            if (ledgerResult.IsError)
                return Errors.Inventory.SupplierLedgerEntryInvalid;

            await supplierLedgerEntryRepository.AddAsync(ledgerResult.Value, cancellationToken);
        }

        return Result.Success;
    }

    private static decimal ComputeUnitCost(decimal totalPurchaseCost, decimal quantity) =>
        decimal.Round(totalPurchaseCost / quantity, 2, MidpointRounding.AwayFromZero);
}
