using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.PurchaseReceipts.Commands.CorrectReceiptCost;

/// <summary>
/// Stages a cost correction's writes: the corrected receipt, the touched batch row, the
/// <c>CostAdjusted</c> ledger entry and the adjustment record. It never saves.
/// </summary>
public sealed class ReceiptCostCorrectionWriter(
    IPurchaseReceiptValueRepository purchaseReceiptValueRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository,
    IPurchaseReceiptCostAdjustmentRepository costAdjustmentRepository)
{
    internal async Task StageCorrectionAsync(
        CorrectReceiptCostCommand command, PurchaseReceiptValue receipt, InventoryBatch batch,
        SupplierLedgerEntry ledgerEntry, PurchaseReceiptCostAdjustment adjustment, CancellationToken cancellationToken)
    {
        // Written against the version the correction was entered at: a second correction of the
        // same version fails here instead of posting its delta on top of the first. The batch row is
        // written too, so a void of the batch racing this correction fails one of the two saves.
        purchaseReceiptValueRepository.Update(receipt);
        batch.MarkUpdatedBy(command.ActorUserId);
        inventoryBatchRepository.Update(batch);
        await supplierLedgerEntryRepository.AddAsync(ledgerEntry, cancellationToken);
        await costAdjustmentRepository.AddAsync(adjustment, cancellationToken);
    }
}
