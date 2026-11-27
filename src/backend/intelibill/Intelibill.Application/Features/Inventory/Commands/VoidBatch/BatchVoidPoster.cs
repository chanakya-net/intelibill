using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Commands.VoidBatch;

/// <summary>
/// Stages a batch void's writes: the voided batch, its reversal movement, the payable reversal, the
/// receipt reversal fact, and the receipt and stock rows. It never saves.
/// </summary>
public sealed class BatchVoidPoster(
    IInventoryBatchRepository inventoryBatchRepository,
    IStockTransactionRepository stockTransactionRepository,
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository,
    IPurchaseReceiptValueRepository purchaseReceiptValueRepository,
    IInventoryRepository inventoryRepository,
    IPurchaseReceiptReversalRepository receiptReversals,
    IBusinessClock businessClock)
{
    internal sealed record LedgerReversal(Guid? LedgerEntryId, decimal? Amount);

    internal void StageVoidedBatch(VoidBatchCommand command, InventoryBatch batch)
    {
        batch.Void(command.ActorUserId);
        inventoryBatchRepository.Update(batch);
    }

    internal async Task<ErrorOr<StockTransaction>> StageReversalMovementAsync(
        VoidBatchCommand command,
        InventoryBatch batch,
        decimal originalQuantity,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var stockTransactionResult = StockTransaction.Create(
            command.ActiveShopId,
            batch.ItemId,
            batch.Id,
            StockTransactionType.Reversal,
            -originalQuantity,
            null,
            "Batch voided by owner",
            now,
            command.ActorUserId,
            command.ActorUserId);

        if (stockTransactionResult.IsError)
            return stockTransactionResult.Errors;

        await stockTransactionRepository.AddAsync(stockTransactionResult.Value, cancellationToken);

        return stockTransactionResult.Value;
    }

    internal async Task<ErrorOr<LedgerReversal>> StageLedgerReversalAsync(
        VoidBatchCommand command,
        Shop shop,
        InventoryBatch batch,
        PurchaseReceiptValue receiptValue,
        Guid? supplierId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Guid? reversalLedgerId = null;
        decimal? ledgerReversalAmount = null;
        var reversalAmount = -receiptValue.EffectiveTotalCost;

        if (supplierId.HasValue && reversalAmount != 0)
        {
            ledgerReversalAmount = reversalAmount;
            var ledgerResult = SupplierLedgerEntry.Create(
                command.ActiveShopId,
                supplierId.Value,
                batch.Id,
                SupplierLedgerEntryType.Reversal,
                ledgerReversalAmount.Value,
                businessClock.GetBusinessDate(now, shop.BusinessTimeZoneId),
                "Batch voided — supplier ledger corrected",
                command.ActorUserId);

            if (ledgerResult.IsError)
                return Errors.Inventory.SupplierLedgerEntryInvalid;

            reversalLedgerId = ledgerResult.Value.Id;
            await supplierLedgerEntryRepository.AddAsync(ledgerResult.Value, cancellationToken);
        }

        return new LedgerReversal(reversalLedgerId, ledgerReversalAmount);
    }

    internal async Task<ErrorOr<Success>> StageReceiptReversalAsync(
        VoidBatchCommand command,
        Shop shop,
        PurchaseReceiptValue receiptValue,
        Guid stockTransactionId,
        Guid? reversalLedgerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var exactAmount = receiptValue.EffectiveTotalCost;
        var fact = PurchaseReceiptReversal.Create(
            command.ActiveShopId,
            receiptValue.Id,
            null,
            command.ActorUserId,
            "Receipt voided by owner",
            now,
            businessClock.Today(shop.BusinessTimeZoneId),
            shop.BusinessTimeZoneId,
            receiptValue.ReceivedQuantity,
            exactAmount,
            stockTransactionId,
            reversalLedgerId,
            $"RR-{Guid.NewGuid():N}");
        if (fact.IsError)
            return fact.Errors;

        await receiptReversals.AddAsync(fact.Value, cancellationToken);

        return Result.Success;
    }

    internal ErrorOr<Success> StageReceiptAndStockRemoval(
        VoidBatchCommand command,
        PurchaseReceiptValue? receiptValue,
        Domain.Entities.Inventory inventory,
        decimal remainingQuantity)
    {
        // Written against the version it was read at, like the batch: a cost correction committing in
        // between fails this save, so the void never reverses a stale total.
        if (receiptValue is not null)
            purchaseReceiptValueRepository.Update(receiptValue);

        var subtractResult = inventory.SubtractQuantity(remainingQuantity, command.ActorUserId);
        if (subtractResult.IsError)
            return subtractResult.Errors;

        inventoryRepository.Update(inventory);

        return Result.Success;
    }
}
