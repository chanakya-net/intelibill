using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Features.SupplierReturns.Services;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Application.Features.Inventory.Commands.ReassignBatchSupplier;

public sealed class ReassignBatchSupplierCommandHandler(
    IUserRepository userRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository,
    ISupplierRepository supplierRepository,
    IPurchaseReceiptValueRepository purchaseReceiptValueRepository,
    IPaymentReceiptRepository paymentReceiptRepository,
    IShopRepository shopRepository,
    IPostingPeriodPolicy postingPeriodPolicy,
    IBusinessClock businessClock,
    IUnitOfWork unitOfWork,
    ISupplierReturnMutationLock supplierReturnLock,
    IShopLicenceFence licenceFence,
    ILiveSessionAuthority authority)
{
    [LicenceAction(LicenceAction.SourceCorrection)]
    public async Task<ErrorOr<Success>> HandleAsync(ReassignBatchSupplierCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role != ShopRole.Owner)
            return Errors.Supplier.UserIsNotOwner;

        var shop = await shopRepository.GetByIdAsync(command.ActiveShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        return await PostingTransaction.RunAsync(
            unitOfWork,
            ct => ReassignAsync(command, shop, ct),
            cancellationToken);
    }

    private async Task<ErrorOr<Success>> ReassignAsync(
        ReassignBatchSupplierCommand command,
        Shop shop,
        CancellationToken cancellationToken)
    {
        var licence = await licenceFence.EnterForSessionAsync(
            authority,
            command.Session,
            command.ActorUserId,
            command.ActiveShopId,
            LicenceAction.SourceCorrection,
            cancellationToken);
        if (licence.IsError) return licence.Errors;
        if (licence.Value.CurrentDecision().EnforcedDenial() is { } denial) return denial;

        var batchOrError = await LoadReassignableBatchAsync(command, cancellationToken);
        if (batchOrError.IsError)
            return batchOrError.Errors;

        var batch = batchOrError.Value;

        // Read before the ledger entries: a cost correction writes the receipt and the batch, so one
        // committing after these reads fails the save below instead of being left behind.
        var receiptValue = await purchaseReceiptValueRepository.GetByBatchAsync(
            command.ActiveShopId,
            batch.Id,
            cancellationToken);
        if (await HasActiveReceiptAllocationsAsync(command, receiptValue, cancellationToken))
        {
            // The allocations belong to the current supplier's payments; moving the payable would strand them.
            return Errors.Payment.DocumentHasAllocations;
        }

        var batchEntries = await supplierLedgerEntryRepository.GetByBatchAsync(
            command.ActiveShopId,
            batch.Id,
            cancellationToken);
        var goodsReceivedEntry = batchEntries.FirstOrDefault(e => e.EntryType == SupplierLedgerEntryType.GoodsReceived);

        var newSupplierOrError = await ResolveNewSupplierAsync(
            command,
            batch,
            goodsReceivedEntry,
            cancellationToken);
        if (newSupplierOrError.IsError)
            return newSupplierOrError.Errors;

        var newSupplier = newSupplierOrError.Value;

        var periodFence = await AuthorizePostingPeriodsAsync(command, shop, batchEntries, cancellationToken);
        if (periodFence.IsError)
            return periodFence.Errors;

        var supplierLock = await TakeReceiptSupplierLockAsync(command, receiptValue, cancellationToken);
        if (supplierLock.IsError)
            return supplierLock.Errors;

        if (licence.Value.CurrentDecision().ExistingSourceDenial(receiptValue?.ReceivedAt ?? batch.CreatedAt) is { } sourceDenial)
            return sourceDenial;

        ReassignLedgerEntries(goodsReceivedEntry, batchEntries, newSupplier.Id);

        if (receiptValue?.HasSupplierReturns == true)
            return Error.Conflict("PurchaseReceipt.HasSupplierReturns", "This receipt has supplier shipments; its supplier cannot change.");

        if (receiptValue is not null)
        {
            receiptValue.ReassignSupplier(newSupplier.Id);
            purchaseReceiptValueRepository.Update(receiptValue);
        }

        batch.AssignSupplier(newSupplier.Id, command.ActorUserId);
        inventoryBatchRepository.Update(batch);

        return await SaveAsync(cancellationToken);
    }

    private async Task<ErrorOr<InventoryBatch>> LoadReassignableBatchAsync(
        ReassignBatchSupplierCommand command,
        CancellationToken cancellationToken)
    {
        var batch = await inventoryBatchRepository.GetByIdAsync(command.BatchId, cancellationToken);
        if (batch is null || batch.ShopId != command.ActiveShopId)
            return Errors.Inventory.BatchNotFound;

        // A voided receipt's payable was reversed against its supplier; moving part of it would
        // leave the new supplier owing for goods that were never kept.
        if (batch.IsVoided)
            return Errors.Inventory.BatchAlreadyVoided;

        return batch;
    }

    private async Task<bool> HasActiveReceiptAllocationsAsync(
        ReassignBatchSupplierCommand command,
        PurchaseReceiptValue? receiptValue,
        CancellationToken cancellationToken) =>
        receiptValue is not null
        && await paymentReceiptRepository.HasActiveAllocationsAsync(
            command.ActiveShopId,
            PaymentAllocationTargetType.SupplierReceipt,
            receiptValue.Id,
            cancellationToken);

    /// <summary>
    /// Checks the batch's current supplier is the system placeholder and returns the real supplier to move it to.
    /// </summary>
    private async Task<ErrorOr<Supplier>> ResolveNewSupplierAsync(
        ReassignBatchSupplierCommand command,
        InventoryBatch batch,
        SupplierLedgerEntry? goodsReceivedEntry,
        CancellationToken cancellationToken)
    {
        // A free receipt has no payable; its batch alone records who supplied it.
        var currentSupplierId = goodsReceivedEntry?.SupplierId ?? (batch.CostPrice == 0 ? batch.SupplierId : null);
        if (currentSupplierId is null)
            return Errors.Inventory.SupplierLedgerEntryInvalid;

        var currentSupplier = await supplierRepository.GetByIdAsync(currentSupplierId.Value, cancellationToken);
        if (currentSupplier is null || currentSupplier.ShopId != command.ActiveShopId)
            return Errors.Supplier.SupplierNotFound;

        if (!currentSupplier.IsSystem)
            return Errors.Supplier.CannotReassignFromRealSupplier;

        var newSupplier = await supplierRepository.GetByIdAsync(command.NewSupplierId, cancellationToken);
        if (newSupplier is null || newSupplier.ShopId != command.ActiveShopId)
            return Errors.Supplier.SupplierNotFound;

        if (newSupplier.IsSystem)
            return Errors.Supplier.CannotModifySystemSupplier;

        return newSupplier;
    }

    private async Task<ErrorOr<Success>> AuthorizePostingPeriodsAsync(
        ReassignBatchSupplierCommand command,
        Shop shop,
        IReadOnlyList<SupplierLedgerEntry> batchEntries,
        CancellationToken cancellationToken)
    {
        // The posted entries move to another supplier in their own months, so every one of those
        // months (and today, for the batch and receipt rows) must be open.
        var rewrittenEntries = batchEntries
            .Where(e => e.EntryType is SupplierLedgerEntryType.GoodsReceived or SupplierLedgerEntryType.CostAdjusted)
            .ToList();
        var fence = await postingPeriodPolicy.AuthorizeForWriteAsync(
            command.ActiveShopId,
            [businessClock.Today(shop.BusinessTimeZoneId), .. rewrittenEntries.Select(e => e.EntryDate)],
            cancellationToken);
        if (fence.IsError)
            return fence.Errors;

        return Result.Success;
    }

    private async Task<ErrorOr<Success>> TakeReceiptSupplierLockAsync(
        ReassignBatchSupplierCommand command,
        PurchaseReceiptValue? receiptValue,
        CancellationToken cancellationToken)
    {
        // After the period fence and before any row this command writes, so it cannot deadlock with
        // a receipt reversal, which locks the receipt's current supplier first: see
        // SupplierReceiptWriteLock. A batch with no receipt row has nothing a reversal can hold.
        if (receiptValue?.SupplierId is { } receiptSupplierId)
        {
            var supplierLock = await supplierReturnLock.TakeBeforeReceiptWriteAsync(
                command.ActiveShopId,
                receiptSupplierId,
                Errors.Supplier.SupplierNotFound,
                cancellationToken);
            if (supplierLock.IsError)
                return supplierLock.Errors;
        }

        return Result.Success;
    }

    private void ReassignLedgerEntries(
        SupplierLedgerEntry? goodsReceivedEntry,
        IReadOnlyList<SupplierLedgerEntry> batchEntries,
        Guid newSupplierId)
    {
        if (goodsReceivedEntry is not null)
        {
            goodsReceivedEntry.ReassignSupplier(newSupplierId);
            supplierLedgerEntryRepository.Update(goodsReceivedEntry);
        }

        // Cost corrections belong to the same receipt: they follow it, or the placeholder supplier
        // would keep owing (or being owed) the difference.
        foreach (var costAdjustment in batchEntries.Where(e => e.EntryType == SupplierLedgerEntryType.CostAdjusted))
        {
            costAdjustment.ReassignSupplier(newSupplierId);
            supplierLedgerEntryRepository.Update(costAdjustment);
        }
    }

    private async Task<ErrorOr<Success>> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Errors.Inventory.ConcurrentChange;
        }

        return Result.Success;
    }
}
