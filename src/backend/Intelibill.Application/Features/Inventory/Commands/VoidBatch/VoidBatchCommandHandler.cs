using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.UpdateInventoryBatch;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Events;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace Intelibill.Application.Features.Inventory.Commands.VoidBatch;

/// <summary>
/// Voids a receipt that nothing has consumed: one <c>Reversal(-quantity)</c> movement, the same
/// quantity off the aggregate and the payable reversed, all in one save.
/// </summary>
/// <remarks>
/// A receipt that was sold from, returned to or adjusted is refused with
/// <c>Inventory.BatchConsumed</c>: reversing its whole payable would erase a purchase that partly
/// happened, and its movements no longer describe a single receipt. Remaining stock is removed with a
/// stock count, which leaves the payable alone; returning goods to the supplier is a separate flow.
/// </remarks>
public sealed class VoidBatchCommandHandler(
    IUserRepository userRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IStockTransactionRepository stockTransactionRepository,
    IInventoryAdjustmentRepository inventoryAdjustmentRepository,
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository,
    IPurchaseReceiptValueRepository purchaseReceiptValueRepository,
    IPaymentReceiptRepository paymentReceiptRepository,
    IInventoryRepository inventoryRepository,
    IUnitOfWork unitOfWork,
    IMessageBus messageBus)
{
    public async Task<ErrorOr<VoidBatchResultDto>> HandleAsync(VoidBatchCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not ShopRole.Owner)
            return Errors.Inventory.UserIsNotOwner;

        var batch = await inventoryBatchRepository.GetByIdAsync(command.BatchId, cancellationToken);
        if (batch is null || batch.ShopId != command.ActiveShopId)
            return Errors.Inventory.BatchNotFound;

        if (batch.IsVoided)
            return Errors.Inventory.BatchAlreadyVoided;

        if (await IsConsumedAsync(batch, cancellationToken))
            return Errors.Inventory.BatchConsumed;

        var inventory = await inventoryRepository.GetByItemAsync(command.ActiveShopId, batch.ItemId, cancellationToken);
        if (inventory is null)
            return Errors.Inventory.InventoryAggregateNotFound;

        // What is owed for this receipt is its effective total (original plus cost corrections), so
        // the reversal nets the payable to zero, posted to the supplier the receipt is owed to.
        // Receipts older than receipt values fall back to the unit cost they were posted with.
        var receiptValue = await purchaseReceiptValueRepository.GetByBatchAsync(command.ActiveShopId, batch.Id, cancellationToken);
        if (receiptValue is not null
            && await paymentReceiptRepository.HasActiveAllocationsAsync(
                command.ActiveShopId, PaymentAllocationTargetType.SupplierReceipt, receiptValue.Id, cancellationToken))
        {
            // Voiding would erase a payable that payments settled; reverse those payments first.
            return Errors.Payment.DocumentHasAllocations;
        }

        var remainingQuantity = batch.Quantity;
        var originalQuantity = batch.OriginalQuantity;
        var costPrice = batch.CostPrice;
        var supplierId = receiptValue?.SupplierId ?? batch.SupplierId;
        var itemId = batch.ItemId;

        batch.Void(command.ActorUserId);
        inventoryBatchRepository.Update(batch);

        var now = DateTimeOffset.UtcNow;
        var stockTransactionResult = StockTransaction.Create(
            command.ActiveShopId,
            itemId,
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

        decimal? ledgerReversalAmount = null;
        var reversalAmount = receiptValue is { IsResolved: true, EffectiveTotalCost: { } effectiveTotal }
            ? -effectiveTotal
            : -decimal.Round(originalQuantity * costPrice, 2, MidpointRounding.AwayFromZero);

        // A free receipt never created a payable, so there is nothing to reverse. A tiny legacy
        // priced receipt can also round to a zero reversal; that one still owes, so it is refused as
        // before. A receipt value's zero total is exact (corrected down to nothing), so it is not.
        if (supplierId.HasValue && reversalAmount == 0 && receiptValue is not { IsResolved: true })
        {
            var payables = await supplierLedgerEntryRepository.GetByBatchAsync(command.ActiveShopId, batch.Id, cancellationToken);
            if (payables.Any(e => e.EntryType == SupplierLedgerEntryType.GoodsReceived && e.Amount != 0))
                return Errors.Inventory.SupplierLedgerEntryInvalid;
        }

        if (supplierId.HasValue && reversalAmount != 0)
        {
            ledgerReversalAmount = reversalAmount;
            var ledgerResult = SupplierLedgerEntry.Create(
                command.ActiveShopId,
                supplierId.Value,
                batch.Id,
                SupplierLedgerEntryType.Reversal,
                ledgerReversalAmount.Value,
                DateOnly.FromDateTime(now.UtcDateTime),
                "Batch voided — supplier ledger corrected",
                command.ActorUserId);

            if (ledgerResult.IsError)
                return Errors.Inventory.SupplierLedgerEntryInvalid;

            await supplierLedgerEntryRepository.AddAsync(ledgerResult.Value, cancellationToken);
        }

        // Written against the version it was read at, like the batch: a cost correction committing in
        // between fails this save, so the void never reverses a stale total.
        if (receiptValue is not null)
            purchaseReceiptValueRepository.Update(receiptValue);

        var subtractResult = inventory.SubtractQuantity(remainingQuantity, command.ActorUserId);
        if (subtractResult.IsError)
            return subtractResult.Errors;

        inventoryRepository.Update(inventory);

        // A concurrent sale or count of this batch or item fails the row-version check here, and
        // nothing of the void is kept.
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Errors.Inventory.ConcurrentChange;
        }

        await messageBus.PublishAsync(
            new InventoryBatchVoidedDomainEvent(
                command.ActiveShopId,
                itemId,
                batch.Id));

        return new VoidBatchResultDto(
            batch.Id,
            originalQuantity,
            remainingQuantity,
            ledgerReversalAmount);
    }

    private async Task<bool> IsConsumedAsync(InventoryBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Quantity != batch.OriginalQuantity)
            return true;

        var adjustments = await inventoryAdjustmentRepository.GetByBatchAsync(batch.ShopId, batch.Id, cancellationToken);
        if (adjustments.Any(a => !a.IsVoided))
            return true;

        // Equal quantities can still hide a sale that was returned. Movements of voided adjustments
        // net to zero, and a legacy correction's opening movement belongs to the receipt itself.
        var adjustmentNumbers = adjustments.Select(a => a.AdjustmentNumber).ToHashSet(StringComparer.Ordinal);
        var movements = await stockTransactionRepository.GetByBatchAsync(batch.ShopId, batch.Id, cancellationToken);
        return movements.Any(m =>
            m.TransactionType is StockTransactionType.Out or StockTransactionType.Ret or StockTransactionType.Rej
                or StockTransactionType.Dmg or StockTransactionType.Stol
            && !(m.ReferenceNumber is { } reference
                && (adjustmentNumbers.Contains(reference) || reference == UpdateInventoryBatchCommandHandler.CorrectionReference)));
    }
}
