using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.PurchaseReceipts.Commands.CorrectReceiptCost;

/// <summary>
/// Reads the receipt and batch a cost correction acts on, before and after the supplier lock, and
/// decides whether that source is still eligible under the shop's licence.
/// </summary>
public sealed class ReceiptCostSourceReader(
    IPurchaseReceiptValueRepository purchaseReceiptValueRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    ISupplierReturnRepository supplierReturns,
    IBusinessClock businessClock)
{
    internal sealed record LockedReceipt(PurchaseReceiptValue Receipt, InventoryBatch Batch, Guid SupplierId);

    /// <summary>Reads the source before the supplier lock and returns the supplier whose lock the correction takes.</summary>
    internal async Task<ErrorOr<Guid>> ReadSourceSupplierAsync(
        CorrectReceiptCostCommand command, ShopLicenceLease lease, DateTimeOffset performedAt, CancellationToken ct)
    {
        var receipt = (await purchaseReceiptValueRepository.FindAsync(
            r => r.Id == command.ReceiptValueId && r.ShopId == command.ActiveShopId, ct)).SingleOrDefault();
        if (lease.CurrentDecision().EnforcedDenial() is { } readDenial)
            return readDenial;
        if (receipt is null)
            return Errors.PurchaseReceipt.NotFound;
        if (ValidateReceipt(receipt) is { } invalid)
            return invalid;
        if (receipt.Version != command.ExpectedVersion)
            return Errors.PurchaseReceipt.VersionConflict;
        if (receipt.SupplierId is not { } supplierId)
            return Errors.PurchaseReceipt.SupplierMissing;
        if (CheckCompletion(lease, receipt, performedAt) is { } ineligible)
            return ineligible;

        var batch = await ReadBatchAsync(receipt, ct);
        if (CheckCompletion(lease, receipt, performedAt) is { } batchDenial)
            return batchDenial;
        if (batch.IsError)
            return batch.Errors;

        return supplierId;
    }

    internal async Task<ErrorOr<LockedReceipt>> ReloadSourceAsync(
        CorrectReceiptCostCommand command, Guid supplierId, ShopLicenceLease lease,
        DateTimeOffset performedAt, CancellationToken ct)
    {
        var receipt = (await supplierReturns.GetReceiptsAsync(command.ActiveShopId, [command.ReceiptValueId], ct))
            .SingleOrDefault();
        if (lease.CurrentDecision().EnforcedDenial() is { } reloadDenial)
            return reloadDenial;
        if (receipt is null)
            return Errors.PurchaseReceipt.NotFound;
        if (receipt.SupplierId != supplierId || receipt.Version != command.ExpectedVersion)
            return Errors.PurchaseReceipt.VersionConflict;
        if (ValidateReceipt(receipt) is { } invalid)
            return invalid;
        if (CheckCompletion(lease, receipt, performedAt) is { } ineligible)
            return ineligible;

        var batch = await ReadBatchAsync(receipt, ct);
        if (CheckCompletion(lease, receipt, performedAt) is { } batchDenial)
            return batchDenial;
        return batch.IsError ? batch.Errors : new LockedReceipt(receipt, batch.Value, supplierId);
    }

    internal Error? CheckCompletion(ShopLicenceLease lease, PurchaseReceiptValue receipt, DateTimeOffset performedAt)
    {
        var decision = lease.CurrentDecision();
        if (decision.EnforcedDenial() is { } denied)
            return denied;
        if (decision is not { Enforced: true, Outcome: LicenceOutcome.RequiresProvenSource })
            return null;

        return receipt.ReceivedAt == default || receipt.ReceivedAt > performedAt ||
            receipt.ReceivedAt > businessClock.UtcNow || receipt.ReceivedQuantity <= 0
            ? Errors.PurchaseReceipt.SourceIneligible : null;
    }

    private async Task<ErrorOr<InventoryBatch>> ReadBatchAsync(PurchaseReceiptValue receipt, CancellationToken ct)
    {
        var batch = (await inventoryBatchRepository.FindAsync(
            b => b.Id == receipt.InventoryBatchId && b.ShopId == receipt.ShopId && b.ItemId == receipt.ItemId, ct))
            .SingleOrDefault();
        if (batch is null)
            return Errors.Inventory.BatchNotFound;
        if (batch.IsVoided)
            return Errors.PurchaseReceipt.BatchVoided;
        return batch;
    }

    private static Error? ValidateReceipt(PurchaseReceiptValue receipt)
    {
        if (receipt.HasSupplierReturns)
            return Error.Conflict("PurchaseReceipt.HasSupplierReturns", "This receipt has supplier shipments; its cost cannot change.");
        return null;
    }
}
