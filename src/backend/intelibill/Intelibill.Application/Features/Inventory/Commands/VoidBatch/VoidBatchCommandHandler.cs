using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
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
    IUnitOfWork unitOfWork,
    IMessageBus messageBus,
    IShopRepository shopRepository,
    IPostingPeriodPolicy postingPeriodPolicy,
    IBusinessClock businessClock,
    ISupplierReturnMutationLock supplierReturnLock,
    IShopLicenceFence licenceFence,
    ILiveSessionAuthority authority,
    BatchVoidSourceReader sourceReader,
    BatchVoidPoster poster)
{
    [LicenceAction(LicenceAction.SourceCorrection)]
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

    private sealed record ReversibleReceipt(PurchaseReceiptValue ReceiptValue, Domain.Entities.Inventory Inventory);

    private async Task<ErrorOr<VoidOutcome>> VoidAsync(VoidBatchCommand command, Shop shop, CancellationToken cancellationToken)
    {
        var licence = await licenceFence.EnterForSessionAsync(
            authority,
            command.Session,
            command.ActorUserId,
            command.ActiveShopId,
            LicenceAction.SourceCorrection,
            cancellationToken);
        if (licence.IsError)
            return licence.Errors;
        if (licence.Value.CurrentDecision().EnforcedDenial() is { } denial)
            return denial;

        var fence = await postingPeriodPolicy.AuthorizeForWriteAsync(
            command.ActiveShopId,
            businessClock.Today(shop.BusinessTimeZoneId),
            cancellationToken);
        if (fence.IsError)
            return fence.Errors;

        var batchResult = await sourceReader.LoadVoidableBatchAsync(command, cancellationToken);
        if (batchResult.IsError)
            return batchResult.Errors;

        var batch = batchResult.Value;

        var receiptResult = await LoadReversibleReceiptAsync(command, shop, batch, cancellationToken);
        if (receiptResult.IsError)
            return receiptResult.Errors;

        var receiptValue = receiptResult.Value.ReceiptValue;
        var inventory = receiptResult.Value.Inventory;
        var remainingQuantity = batch.Quantity;
        var originalQuantity = batch.OriginalQuantity;
        var supplierId = receiptValue.SupplierId;
        var itemId = batch.ItemId;

        // After both period fences and before any row this command writes, so it cannot deadlock
        // with a supplier return shipment or a receipt reversal: see SupplierReceiptWriteLock.
        var supplierLock = await supplierReturnLock.TakeBeforeReceiptWriteAsync(
            command.ActiveShopId,
            receiptValue.SupplierId!.Value, // LoadReversibleReceiptAsync refused a receipt without a supplier
            BatchVoidSourceReader.SupplierMissingError(),
            cancellationToken);
        if (supplierLock.IsError)
            return supplierLock.Errors;

        if (licence.Value.CurrentDecision().ExistingSourceDenial(receiptValue.ReceivedAt) is { } sourceDenial)
            return sourceDenial;

        poster.StageVoidedBatch(command, batch);

        var now = businessClock.UtcNow;
        var stockTransactionResult = await poster.StageReversalMovementAsync(
            command,
            batch,
            originalQuantity,
            now,
            cancellationToken);
        if (stockTransactionResult.IsError)
            return stockTransactionResult.Errors;

        var ledgerReversalResult = await poster.StageLedgerReversalAsync(
            command,
            shop,
            batch,
            receiptValue,
            supplierId,
            now,
            cancellationToken);
        if (ledgerReversalResult.IsError)
            return ledgerReversalResult.Errors;

        var ledgerReversal = ledgerReversalResult.Value;

        var reversalFact = await poster.StageReceiptReversalAsync(
            command,
            shop,
            receiptValue,
            stockTransactionResult.Value.Id,
            ledgerReversal.LedgerEntryId,
            now,
            cancellationToken);
        if (reversalFact.IsError)
            return reversalFact.Errors;

        var stockRemoval = poster.StageReceiptAndStockRemoval(command, receiptValue, inventory, remainingQuantity);
        if (stockRemoval.IsError)
            return stockRemoval.Errors;

        var saved = await SaveVoidAsync(cancellationToken);
        if (saved.IsError)
            return saved.Errors;

        return new VoidOutcome(
            itemId,
            batch.Id,
            new VoidBatchResultDto(batch.Id, originalQuantity, remainingQuantity, ledgerReversal.Amount));
    }

    private async Task<ErrorOr<ReversibleReceipt>> LoadReversibleReceiptAsync(
        VoidBatchCommand command,
        Shop shop,
        InventoryBatch batch,
        CancellationToken cancellationToken)
    {
        var receiptResult = await sourceReader.LoadSourceReceiptAsync(command, batch, cancellationToken);
        if (receiptResult.IsError)
            return receiptResult.Errors;

        var receiptValue = receiptResult.Value;

        var originalFence = await postingPeriodPolicy.AuthorizeForWriteAsync(
            command.ActiveShopId,
            businessClock.GetBusinessDate(receiptValue.ReceivedAt, shop.BusinessTimeZoneId),
            cancellationToken);
        if (originalFence.IsError)
            return originalFence.Errors;

        var inventory = await sourceReader.LoadReversibleInventoryAsync(command, batch, receiptValue, cancellationToken);
        if (inventory.IsError)
            return inventory.Errors;

        return new ReversibleReceipt(receiptValue, inventory.Value);
    }

    private async Task<ErrorOr<Success>> SaveVoidAsync(CancellationToken cancellationToken)
    {
        // A concurrent sale or count of this batch or item fails the row-version check here, and
        // nothing of the void is kept.
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (unitOfWork.IsConcurrencyConflict(ex))
        {
            return Errors.Inventory.ConcurrentChange;
        }

        return Result.Success;
    }
}
