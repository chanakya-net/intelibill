using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Features.PurchaseReceipts.DTOs;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Application.Features.PurchaseReceipts.Commands.CorrectReceiptCost;

/// <summary>
/// Corrects what a receipt cost by posting only the difference to the supplier payable.
/// </summary>
/// <remarks>
/// <para>
/// <c>delta = revised total − current effective total</c>, never recomputed from remaining stock:
/// ten bought at ₹100 and four sold, a revised total of ₹1100 owes ₹100 more. One
/// <see cref="PurchaseReceiptCostAdjustment"/> and one <c>CostAdjusted</c> ledger entry commit with the
/// receipt's new effective total, or nothing does.
/// </para>
/// <para>
/// The batch's unit cost and every sale's cost snapshot stay as they are; how the difference is
/// treated in valuation is an accounting decision outside this command. A receipt whose original
/// total is not on record is refused rather than estimated.
/// </para>
/// </remarks>
public sealed class CorrectReceiptCostCommandHandler(
    IUserRepository userRepository,
    IPurchaseReceiptValueRepository purchaseReceiptValueRepository,
    IPurchaseReceiptCostAdjustmentRepository costAdjustmentRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository,
    IPaymentReceiptRepository paymentReceiptRepository,
    IUnitOfWork unitOfWork,
    IFinancialOperationExecutor operationExecutor,
    IShopRepository shopRepository,
    IPostingPeriodPolicy postingPeriodPolicy,
    IBusinessClock businessClock)
{
    private static readonly CorrectReceiptCostCommandValidator Validator = new();

    public async Task<ErrorOr<ReceiptCostCorrectionResultDto>> HandleAsync(
        CorrectReceiptCostCommand command,
        CancellationToken cancellationToken)
    {
        var validation = Validator.Validate(command);
        if (!validation.IsValid)
        {
            return validation.Errors
                .Select(e => Error.Validation(e.ErrorCode, e.ErrorMessage))
                .ToList();
        }

        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        // Changes what the shop owes a supplier, like voiding a receipt: the owner's call.
        if (actorMembership.Role is not ShopRole.Owner)
            return Errors.Inventory.UserIsNotOwner;

        var receipt = await purchaseReceiptValueRepository.GetByIdAsync(command.ReceiptValueId, cancellationToken);
        if (receipt is null || receipt.ShopId != command.ActiveShopId)
            return Errors.PurchaseReceipt.NotFound;

        var operationKey = FinancialOperationHasher.NormalizeKey(command.OperationKey);
        if (operationKey is null)
            return await PostingTransaction.RunAsync(unitOfWork, ct => CorrectAsync(receipt, command, ct), cancellationToken);

        var requestHash = FinancialOperationHasher.Compute(new ReceiptCostCorrectionPayload(
            command.ReceiptValueId,
            command.ExpectedVersion,
            FinancialOperationHasher.NormalizeAmount(command.RevisedTotalCost),
            command.Reason.Trim()));

        return await operationExecutor.ExecuteAsync(
            new FinancialOperationScope(command.ActiveShopId, FinancialOperationType.ReceiptCostCorrection, operationKey),
            command.ActorUserId,
            command.ReceiptValueId,
            requestHash,
            ct => CorrectAsync(receipt, command, ct),
            cancellationToken);
    }

    private async Task<ErrorOr<ReceiptCostCorrectionResultDto>> CorrectAsync(
        PurchaseReceiptValue receipt,
        CorrectReceiptCostCommand command,
        CancellationToken cancellationToken)
    {
        var shop = await shopRepository.GetByIdAsync(command.ActiveShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var fence = await postingPeriodPolicy.AuthorizeForWriteAsync(
            command.ActiveShopId,
            businessClock.Today(shop.BusinessTimeZoneId),
            cancellationToken);
        if (fence.IsError)
            return fence.Errors;

        if (receipt.HasSupplierReturns)
            return Error.Conflict("PurchaseReceipt.HasSupplierReturns", "This receipt has supplier shipments; its cost cannot change.");

        if (!receipt.IsResolved || receipt.EffectiveTotalCost is not { } priorTotal)
            return Errors.PurchaseReceipt.EvidenceMissing;

        if (receipt.Version != command.ExpectedVersion)
            return Errors.PurchaseReceipt.VersionConflict;

        var batch = await inventoryBatchRepository.GetByIdAsync(receipt.InventoryBatchId, cancellationToken);
        if (batch is null || batch.ShopId != command.ActiveShopId)
            return Errors.Inventory.BatchNotFound;

        if (batch.IsVoided)
            return Errors.PurchaseReceipt.BatchVoided;

        if (receipt.SupplierId is not { } supplierId)
            return Errors.PurchaseReceipt.SupplierMissing;

        // Payments already allocated to this receipt settled part of its payable; the payable cannot
        // fall below them, or the supplier would show as overpaid on a receipt that was settled.
        var allocated = (await paymentReceiptRepository.GetActiveAllocatedByTargetAsync(
            receipt.ShopId,
            PaymentAllocationTargetType.SupplierReceipt,
            [receipt.Id],
            cancellationToken)).GetValueOrDefault(receipt.Id);
        if (command.RevisedTotalCost < allocated)
            return Errors.Payment.CorrectionBelowAllocated(allocated);

        var applied = receipt.ApplyCostCorrection(command.RevisedTotalCost);
        if (applied.IsError)
            return applied.Errors;

        var delta = applied.Value;
        var performedAt = businessClock.UtcNow;
        var reason = command.Reason.Trim();

        var ledgerEntry = SupplierLedgerEntry.Create(
            command.ActiveShopId,
            supplierId,
            batch.Id,
            SupplierLedgerEntryType.CostAdjusted,
            delta,
            businessClock.GetBusinessDate(performedAt, shop.BusinessTimeZoneId),
            Truncate($"Receipt cost corrected for batch {batch.BatchNumber}: {reason}", 255),
            command.ActorUserId);
        if (ledgerEntry.IsError)
            return Errors.Inventory.SupplierLedgerEntryInvalid;

        var adjustment = PurchaseReceiptCostAdjustment.Create(
            command.ActiveShopId,
            receipt.Id,
            batch.Id,
            priorTotal,
            command.RevisedTotalCost,
            reason,
            ledgerEntry.Value.Id,
            performedAt,
            command.ActorUserId);
        if (adjustment.IsError)
            return adjustment.Errors;

        // Written against the version the correction was entered at: a second correction of the
        // same version fails here instead of posting its delta on top of the first. The batch row is
        // written too, so a void of the batch racing this correction fails one of the two saves.
        purchaseReceiptValueRepository.Update(receipt);
        batch.MarkUpdatedBy(command.ActorUserId);
        inventoryBatchRepository.Update(batch);
        await supplierLedgerEntryRepository.AddAsync(ledgerEntry.Value, cancellationToken);
        await costAdjustmentRepository.AddAsync(adjustment.Value, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Errors.PurchaseReceipt.VersionConflict;
        }

        return new ReceiptCostCorrectionResultDto(
            adjustment.Value.Id,
            receipt.Id,
            batch.Id,
            priorTotal,
            command.RevisedTotalCost,
            delta,
            ledgerEntry.Value.Id,
            receipt.Version,
            performedAt);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        // Never split a surrogate pair: half an emoji cannot be stored as text.
        var length = char.IsHighSurrogate(value[maxLength - 1]) ? maxLength - 1 : maxLength;
        return value[..length];
    }

    private sealed record ReceiptCostCorrectionPayload(Guid ReceiptValueId, uint ExpectedVersion, decimal RevisedTotalCost, string Reason);
}
