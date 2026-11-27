using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Commands.ReassignBatchSupplier;

/// <summary>
/// Reads a batch's posted supplier ledger entries, decides which real supplier its payable moves to
/// from the placeholder, and moves the entries to it.
/// </summary>
public sealed class BatchSupplierLedgerReassigner(
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository,
    ISupplierRepository supplierRepository)
{
    internal async Task<IReadOnlyList<SupplierLedgerEntry>> LoadBatchEntriesAsync(
        ReassignBatchSupplierCommand command,
        InventoryBatch batch,
        CancellationToken cancellationToken) =>
        await supplierLedgerEntryRepository.GetByBatchAsync(
            command.ActiveShopId,
            batch.Id,
            cancellationToken);

    /// <summary>
    /// Checks the batch's current supplier is the system placeholder and returns the real supplier to move it to.
    /// </summary>
    internal async Task<ErrorOr<Supplier>> ResolveNewSupplierAsync(
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

    internal void ReassignLedgerEntries(
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
}
