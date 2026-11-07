using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Features.SupplierReturns.Services;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Common.Time;
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
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository,
    IPurchaseReceiptValueRepository purchaseReceiptValueRepository,
    IPaymentReceiptRepository paymentReceiptRepository,
    IInventoryRepository inventoryRepository,
    IUnitOfWork unitOfWork,
    IMessageBus messageBus,
    IShopRepository shopRepository,
    IPostingPeriodPolicy postingPeriodPolicy,
    IBusinessClock businessClock,
    PurchaseReceiptReversalPolicy reversalPolicy,
    IPurchaseReceiptReversalRepository receiptReversals)
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

        var shop = await shopRepository.GetByIdAsync(command.ActiveShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var result = await PostingTransaction.RunAsync(
            unitOfWork,
            ct => VoidAsync(command, shop, ct),
            cancellationToken);
        if (result.IsError)
            return result.Errors;

        // Published once the void is committed: a rolled-back void must not announce itself.
        await messageBus.PublishAsync(
            new InventoryBatchVoidedDomainEvent(
                command.ActiveShopId,
                result.Value.ItemId,
                result.Value.BatchId));

        return result.Value.Result;
    }

    private sealed record VoidOutcome(Guid ItemId, Guid BatchId, VoidBatchResultDto Result);

    private async Task<ErrorOr<VoidOutcome>> VoidAsync(VoidBatchCommand command, Shop shop, CancellationToken cancellationToken)
    {
        var fence = await postingPeriodPolicy.AuthorizeForWriteAsync(
            command.ActiveShopId,
            businessClock.Today(shop.BusinessTimeZoneId),
            cancellationToken);
        if (fence.IsError)
            return fence.Errors;

        var batch = await inventoryBatchRepository.GetByIdAsync(command.BatchId, cancellationToken);
        if (batch is null || batch.ShopId != command.ActiveShopId)
            return Errors.Inventory.BatchNotFound;

        if (batch.IsVoided)
            return Errors.Inventory.BatchAlreadyVoided;

        var receiptValue=await purchaseReceiptValueRepository.GetByBatchAsync(command.ActiveShopId,batch.Id,cancellationToken);
        if(receiptValue is not {IsResolved:true,EffectiveTotalCost:not null})
            return Error.Conflict("PurchaseReceipt.EvidenceMissing","Full reversal requires a resolved source receipt amount. Review and correct the source evidence first.");
        if(receiptValue.SupplierId is null)
            return Error.Conflict("PurchaseReceipt.SupplierMissing","The source receipt needs a supplier before it can be reversed.");
        var originalFence=await postingPeriodPolicy.AuthorizeForWriteAsync(command.ActiveShopId,businessClock.GetBusinessDate(receiptValue.ReceivedAt,shop.BusinessTimeZoneId),cancellationToken);
        if(originalFence.IsError) return originalFence.Errors;
        var allocated=(await paymentReceiptRepository.GetActiveAllocatedByTargetAsync(command.ActiveShopId,PaymentAllocationTargetType.SupplierReceipt,[receiptValue.Id],cancellationToken)).GetValueOrDefault(receiptValue.Id);
        var reasons=await reversalPolicy.GetReasonsAsync(receiptValue,batch,allocated,cancellationToken);
        if(reasons.Count>0) return Error.Conflict(reasons[0],"The receipt has stock, payment or return history and cannot be fully reversed.");
        var inventory=await inventoryRepository.GetByItemAsync(command.ActiveShopId,batch.ItemId,cancellationToken);
        if(inventory is null) return Errors.Inventory.InventoryAggregateNotFound;
        var remainingQuantity = batch.Quantity;
        var originalQuantity = batch.OriginalQuantity;
        var supplierId = receiptValue.SupplierId;
        var itemId = batch.ItemId;

        batch.Void(command.ActorUserId);
        inventoryBatchRepository.Update(batch);

        var now = businessClock.UtcNow;
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

        Guid? reversalLedgerId=null;
        decimal? ledgerReversalAmount = null;
        var reversalAmount=-receiptValue.EffectiveTotalCost.Value;

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

            reversalLedgerId=ledgerResult.Value.Id;
            await supplierLedgerEntryRepository.AddAsync(ledgerResult.Value, cancellationToken);
        }

        if(receiptValue.EffectiveTotalCost is { } exactAmount)
        {
            var fact=PurchaseReceiptReversal.Create(command.ActiveShopId,receiptValue.Id,null,command.ActorUserId,"Receipt voided by owner",now,businessClock.Today(shop.BusinessTimeZoneId),shop.BusinessTimeZoneId,receiptValue.ReceivedQuantity,exactAmount,stockTransactionResult.Value.Id,reversalLedgerId,$"RR-{Guid.NewGuid():N}");
            if(fact.IsError) return fact.Errors;
            await receiptReversals.AddAsync(fact.Value,cancellationToken);
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

        return new VoidOutcome(
            itemId,
            batch.Id,
            new VoidBatchResultDto(batch.Id, originalQuantity, remainingQuantity, ledgerReversalAmount));
    }

}
