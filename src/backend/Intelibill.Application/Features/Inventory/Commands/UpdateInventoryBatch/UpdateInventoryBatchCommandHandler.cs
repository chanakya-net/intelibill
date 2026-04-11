using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.UpdateInventoryBatch;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Commands.UpdateInventoryBatch;

public sealed class UpdateInventoryBatchCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IInventoryRepository inventoryRepository,
    IStockTransactionRepository stockTransactionRepository,
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<Success>> Handle(UpdateInventoryBatchCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", "User not found.");

        var shop = await shopRepository.GetByIdAsync(command.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(command.UserId, command.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role != ShopRole.Owner && membership.Role != ShopRole.Manager)
            return Error.Forbidden("Shop.PermissionDenied", "Only owners or managers can update batches.");

        var originalBatch = await inventoryBatchRepository.GetByIdAsync(command.BatchId, cancellationToken);
        if (originalBatch is null || originalBatch.ShopId != command.ShopId)
            return Errors.Inventory.BatchNotFound;

        if (originalBatch.IsVoided)
            return Error.Validation("Inventory.BatchVoided", "Cannot correct an already voided batch.");

        // 1. Determine new batch number
        var targetBatchNumber = command.NewBatchNumber?.Trim();
        if (string.IsNullOrWhiteSpace(targetBatchNumber))
        {
            // Generate a correction batch number
            targetBatchNumber = $"{originalBatch.BatchNumber}-CORR-{DateTime.UtcNow:HHmm}";
        }

        // Verify uniqueness of the new batch number (if different from old or if we want strictly new rows)
        var existingBatch = await inventoryBatchRepository.GetByBatchNumberAsync(command.ShopId, originalBatch.ItemId, targetBatchNumber, cancellationToken);
        if (existingBatch != null && !existingBatch.IsVoided)
        {
            return Errors.Inventory.BatchNumberAlreadyExists;
        }

        var oldQuantity = originalBatch.Quantity;
        var oldCostPrice = originalBatch.CostPrice;
        var oldSupplierId = originalBatch.SupplierId;

        // 2. Void the original batch
        originalBatch.Void(command.UserId);
        inventoryBatchRepository.Update(originalBatch);

        // 3. Create the new corrected batch row
        var newBatchResult = InventoryBatch.Create(
            command.ShopId,
            originalBatch.ItemId,
            targetBatchNumber,
            command.Quantity,
            command.CostPrice,
            command.Mrp,
            command.SalesPrice,
            command.TaxRatePercent,
            command.TaxIncluded,
            command.ExpiryDate,
            command.ManufacturingDate,
            command.SupplierId,
            command.UserId);

        if (newBatchResult.IsError)
            return newBatchResult.Errors;

        var newBatch = newBatchResult.Value;
        await inventoryBatchRepository.AddAsync(newBatch, cancellationToken);

        // 4. Adjust Inventory (total stock)
        var quantityDifference = command.Quantity - oldQuantity;
        if (quantityDifference != 0)
        {
            var inventory = await inventoryRepository.GetByItemAsync(command.ShopId, originalBatch.ItemId, cancellationToken);
            if (inventory is null)
                return Errors.Inventory.InventoryAggregateNotFound;

            if (quantityDifference > 0)
            {
                var addResult = inventory.AddQuantity(quantityDifference, command.UserId);
                if (addResult.IsError)
                    return addResult.Errors;
            }
            else
            {
                try
                {
                    inventory.SubtractQuantity(Math.Abs(quantityDifference), command.UserId);
                }
                catch (InvalidOperationException ex)
                {
                    return Error.Validation("Inventory.InsufficientStock", ex.Message);
                }
            }

            inventoryRepository.Update(inventory);

            // Record adjustment transaction
            var transaction = StockTransaction.Create(
                command.ShopId,
                originalBatch.ItemId,
                newBatch.Id,
                quantityDifference > 0 ? StockTransactionType.In : StockTransactionType.Out,
                Math.Abs(quantityDifference),
                "Batch Correction",
                $"Corrected from batch {originalBatch.BatchNumber}. Qty adjusted from {oldQuantity} to {command.Quantity}",
                DateTimeOffset.UtcNow,
                command.UserId,
                command.UserId);
            
            if (!transaction.IsError)
            {
                await stockTransactionRepository.AddAsync(transaction.Value, cancellationToken);
            }
        }

        // 5. Adjust Supplier Ledger
        var costChanged = command.CostPrice != oldCostPrice;
        var supplierChanged = command.SupplierId != oldSupplierId;

        if ((quantityDifference != 0 || costChanged || supplierChanged) && (oldSupplierId.HasValue || command.SupplierId.HasValue))
        {
            // Reverse old entry if it exists using RecordAdjusted with negative amount
            if (oldSupplierId.HasValue)
            {
                var existingEntries = await supplierLedgerEntryRepository.GetByBatchAsync(command.ShopId, originalBatch.Id, cancellationToken);
                var goodsReceivedEntry = existingEntries.FirstOrDefault(e => e.EntryType == SupplierLedgerEntryType.GoodsReceived);
                
                if (goodsReceivedEntry != null)
                {
                    var reversal = SupplierLedgerEntry.Create(
                        command.ShopId,
                        oldSupplierId.Value,
                        null, // BatchId null for RecordAdjusted reversal as per integration test
                        SupplierLedgerEntryType.RecordAdjusted,
                        -goodsReceivedEntry.Amount,
                        DateOnly.FromDateTime(DateTime.UtcNow),
                        $"Correction reversal for batch: {originalBatch.BatchNumber}",
                        command.UserId);

                    if (!reversal.IsError)
                    {
                        await supplierLedgerEntryRepository.AddAsync(reversal.Value, cancellationToken);
                    }
                }
            }

            // Create new entry for new supplier/amount
            if (command.SupplierId.HasValue)
            {
                var newAmount = command.Quantity * command.CostPrice;
                if (newAmount > 0)
                {
                    var newEntry = SupplierLedgerEntry.Create(
                        command.ShopId,
                        command.SupplierId.Value,
                        newBatch.Id,
                        SupplierLedgerEntryType.GoodsReceived,
                        newAmount,
                        command.EntryDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                        command.Notes ?? $"Correction for batch: {targetBatchNumber}",
                        command.UserId);

                    if (!newEntry.IsError)
                    {
                        await supplierLedgerEntryRepository.AddAsync(newEntry.Value, cancellationToken);
                    }
                }
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success;
    }
}
