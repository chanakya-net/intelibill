using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.SupplierReturns.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Commands.VoidBatch;

/// <summary>Reads the batch a void acts on and its source receipt, and decides whether the receipt can be fully reversed.</summary>
public sealed class BatchVoidSourceReader(
    IInventoryBatchRepository inventoryBatchRepository,
    IPurchaseReceiptValueRepository purchaseReceiptValueRepository,
    IPaymentReceiptRepository paymentReceiptRepository,
    IInventoryRepository inventoryRepository,
    PurchaseReceiptReversalPolicy reversalPolicy)
{
    internal static Error SupplierMissingError() =>
        Error.Conflict("PurchaseReceipt.SupplierMissing", "The source receipt needs a supplier before it can be reversed.");

    internal async Task<ErrorOr<InventoryBatch>> LoadVoidableBatchAsync(
        VoidBatchCommand command,
        CancellationToken cancellationToken)
    {
        var batch = await inventoryBatchRepository.GetByIdAsync(command.BatchId, cancellationToken);
        if (batch is null || batch.ShopId != command.ActiveShopId)
            return Errors.Inventory.BatchNotFound;

        if (batch.IsVoided)
            return Errors.Inventory.BatchAlreadyVoided;

        return batch;
    }

    internal async Task<ErrorOr<PurchaseReceiptValue>> LoadSourceReceiptAsync(
        VoidBatchCommand command,
        InventoryBatch batch,
        CancellationToken cancellationToken)
    {
        var receiptValue = await purchaseReceiptValueRepository.GetByBatchAsync(
            command.ActiveShopId,
            batch.Id,
            cancellationToken);
        if (receiptValue is null)
        {
            return Error.Conflict(
                "PurchaseReceipt.EvidenceMissing",
                "Full reversal requires the source receipt. Review the source evidence first.");
        }

        if (receiptValue.SupplierId is null)
            return SupplierMissingError();

        return receiptValue;
    }

    internal async Task<ErrorOr<Domain.Entities.Inventory>> LoadReversibleInventoryAsync(
        VoidBatchCommand command,
        InventoryBatch batch,
        PurchaseReceiptValue receiptValue,
        CancellationToken cancellationToken)
    {
        var allocatedByTarget = await paymentReceiptRepository.GetActiveAllocatedByTargetAsync(
            command.ActiveShopId,
            PaymentAllocationTargetType.SupplierReceipt,
            [receiptValue.Id],
            cancellationToken);
        var allocated = allocatedByTarget.GetValueOrDefault(receiptValue.Id);

        var reasons = await reversalPolicy.GetReasonsAsync(receiptValue, batch, allocated, cancellationToken);
        if (reasons.Count > 0)
        {
            return Error.Conflict(
                reasons[0],
                "The receipt has stock, payment or return history and cannot be fully reversed.");
        }

        var inventory = await inventoryRepository.GetByItemAsync(command.ActiveShopId, batch.ItemId, cancellationToken);
        if (inventory is null)
            return Errors.Inventory.InventoryAggregateNotFound;

        return inventory;
    }
}
