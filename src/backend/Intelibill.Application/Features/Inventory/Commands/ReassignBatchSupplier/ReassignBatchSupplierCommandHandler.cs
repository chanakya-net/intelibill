using ErrorOr;
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
    IUnitOfWork unitOfWork)
{
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
        var batch = await inventoryBatchRepository.GetByIdAsync(command.BatchId, cancellationToken);
        if (batch is null || batch.ShopId != command.ActiveShopId)
            return Errors.Inventory.BatchNotFound;

        // A voided receipt's payable was reversed against its supplier; moving part of it would
        // leave the new supplier owing for goods that were never kept.
        if (batch.IsVoided)
            return Errors.Inventory.BatchAlreadyVoided;

        // Read before the ledger entries: a cost correction writes the receipt and the batch, so one
        // committing after these reads fails the save below instead of being left behind.
        var receiptValue = await purchaseReceiptValueRepository.GetByBatchAsync(command.ActiveShopId, batch.Id, cancellationToken);
        if (receiptValue is not null
            && await paymentReceiptRepository.HasActiveAllocationsAsync(
                command.ActiveShopId, PaymentAllocationTargetType.SupplierReceipt, receiptValue.Id, cancellationToken))
        {
            // The allocations belong to the current supplier's payments; moving the payable would strand them.
            return Errors.Payment.DocumentHasAllocations;
        }

        var batchEntries = await supplierLedgerEntryRepository.GetByBatchAsync(command.ActiveShopId, batch.Id, cancellationToken);
        var goodsReceivedEntry = batchEntries.FirstOrDefault(e => e.EntryType == SupplierLedgerEntryType.GoodsReceived);

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

        if (goodsReceivedEntry is not null)
        {
            goodsReceivedEntry.ReassignSupplier(newSupplier.Id);
            supplierLedgerEntryRepository.Update(goodsReceivedEntry);
        }

        // Cost corrections belong to the same receipt: they follow it, or the placeholder supplier
        // would keep owing (or being owed) the difference.
        foreach (var costAdjustment in batchEntries.Where(e => e.EntryType == SupplierLedgerEntryType.CostAdjusted))
        {
            costAdjustment.ReassignSupplier(newSupplier.Id);
            supplierLedgerEntryRepository.Update(costAdjustment);
        }

        if (receiptValue is not null)
        {
            receiptValue.ReassignSupplier(newSupplier.Id);
            purchaseReceiptValueRepository.Update(receiptValue);
        }

        batch.AssignSupplier(newSupplier.Id, command.ActorUserId);
        inventoryBatchRepository.Update(batch);

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
