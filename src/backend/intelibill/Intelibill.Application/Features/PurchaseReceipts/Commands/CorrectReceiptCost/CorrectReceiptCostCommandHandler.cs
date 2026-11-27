using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Features.PurchaseReceipts.DTOs;
using Intelibill.Application.Features.SupplierReturns.Services;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using LockedReceipt = Intelibill.Application.Features.PurchaseReceipts.Commands.CorrectReceiptCost.ReceiptCostSourceReader.LockedReceipt;

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
/// <para>
/// Before writing, the correction takes the supplier's row lock, so it cannot deadlock with a
/// supplier return shipment or a receipt reversal: see <see cref="SupplierReceiptWriteLock"/>.
/// </para>
/// </remarks>
public sealed class CorrectReceiptCostCommandHandler(
    IPaymentReceiptRepository paymentReceiptRepository,
    IUnitOfWork unitOfWork,
    IFinancialOperationExecutor operationExecutor,
    IShopRepository shopRepository,
    IPostingPeriodPolicy postingPeriodPolicy,
    IBusinessClock businessClock,
    ISupplierReturnMutationLock supplierReturnLock,
    IShopLicenceFence licenceFence,
    ILiveSessionAuthority sessions,
    ReceiptCostSourceReader sourceReader,
    ReceiptCostCorrectionWriter writer)
{
    private static readonly CorrectReceiptCostCommandValidator Validator = new();

    [LicenceAction(LicenceAction.SourceCorrection)]
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

        if (await CheckAuthorityAsync(command, cancellationToken) is { } denied)
            return denied;

        var operationKey = FinancialOperationHasher.NormalizeKey(command.OperationKey);
        if (operationKey is null)
            return await PostingTransaction.RunAsync(unitOfWork, ct => CorrectAsync(command, ct), cancellationToken);

        var requestHash = FinancialOperationHasher.Compute(new ReceiptCostCorrectionPayload(
            command.ReceiptValueId,
            command.ExpectedVersion,
            FinancialOperationHasher.NormalizeAmount(command.RevisedTotalCost),
            command.Reason.Trim()));

        return await operationExecutor.ExecuteAsync(
            new FinancialOperationScope(command.ActiveShopId, FinancialOperationType.ReceiptCostCorrection, operationKey)
            {
                RecheckReplayAuthority = ct => CheckAuthorityAsync(command, ct),
            },
            command.ActorUserId,
            command.ReceiptValueId,
            requestHash,
            ct => CorrectAsync(command, ct),
            cancellationToken);
    }

    private async Task<Error?> CheckAuthorityAsync(CorrectReceiptCostCommand command, CancellationToken ct)
    {
        if (command.Session is not { } session || session.UserId != command.ActorUserId ||
            session.ActiveShopId != command.ActiveShopId || !await sessions.IsValidAsync(session, ct))
            return Errors.Auth.SessionInvalidated;
        return session.ActiveShopRole == nameof(ShopRole.Owner) ? null : Errors.Inventory.UserIsNotOwner;
    }

    private async Task<ErrorOr<ReceiptCostCorrectionResultDto>> CorrectAsync(
        CorrectReceiptCostCommand command, CancellationToken ct)
    {
        var lease = await licenceFence.EnterAsync(command.ActiveShopId, LicenceAction.SourceCorrection, ct);
        if (lease.IsError)
            return lease.Errors;
        if (lease.Value.CurrentDecision().EnforcedDenial() is { } denied)
            return denied;
        if (await CheckAuthorityAsync(command, ct) is { } authorityDenied)
            return authorityDenied;

        var shop = await shopRepository.GetByIdAsync(command.ActiveShopId, ct);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var performedAt = businessClock.UtcNow;
        var date = businessClock.GetBusinessDate(performedAt, shop.BusinessTimeZoneId);
        var period = await postingPeriodPolicy.AuthorizeForWriteAsync(command.ActiveShopId, date, ct);
        if (lease.Value.CurrentDecision().EnforcedDenial() is { } periodDenial)
            return periodDenial;
        if (period.IsError)
            return period.Errors;

        var source = await LockSourceAsync(command, lease.Value, performedAt, ct);
        if (source.IsError)
            return source.Errors;

        return await PostDeltaAsync(command, source.Value, lease.Value, performedAt, date, ct);
    }

    private async Task<ErrorOr<LockedReceipt>> LockSourceAsync(
        CorrectReceiptCostCommand command, ShopLicenceLease lease, DateTimeOffset performedAt, CancellationToken ct)
    {
        var supplierRead = await sourceReader.ReadSourceSupplierAsync(command, lease, performedAt, ct);
        if (supplierRead.IsError)
            return supplierRead.Errors;
        var supplierId = supplierRead.Value;
        var supplierLock = await supplierReturnLock.TakeBeforeReceiptWriteAsync(
            command.ActiveShopId, supplierId, Errors.PurchaseReceipt.SupplierMissing, ct);
        if (lease.CurrentDecision().EnforcedDenial() is { } lockDenial)
            return lockDenial;
        if (await CheckAuthorityAsync(command, ct) is { } authorityDenied)
            return authorityDenied;
        if (supplierLock.IsError)
            return supplierLock.Errors;

        return await sourceReader.ReloadSourceAsync(command, supplierId, lease, performedAt, ct);
    }

    private async Task<ErrorOr<ReceiptCostCorrectionResultDto>> PostDeltaAsync(
        CorrectReceiptCostCommand command, LockedReceipt source, ShopLicenceLease lease, DateTimeOffset performedAt,
        DateOnly date, CancellationToken cancellationToken)
    {
        var (receipt, batch, supplierId) = source;
        var priorTotal = receipt.EffectiveTotalCost;
        // Payments already allocated to this receipt settled part of its payable; the payable cannot
        // fall below them, or the supplier would show as overpaid on a receipt that was settled.
        var allocated = (await paymentReceiptRepository.GetActiveAllocatedByTargetAsync(
            receipt.ShopId,
            PaymentAllocationTargetType.SupplierReceipt,
            [receipt.Id],
            cancellationToken)).GetValueOrDefault(receipt.Id);
        if (sourceReader.CheckCompletion(lease, receipt, performedAt) is { } allocationDenial)
            return allocationDenial;
        if (command.RevisedTotalCost < allocated)
            return Errors.Payment.CorrectionBelowAllocated(allocated);

        var applied = receipt.ApplyCostCorrection(command.RevisedTotalCost);
        if (applied.IsError)
            return applied.Errors;

        var delta = applied.Value;
        var reason = command.Reason.Trim();

        var ledgerEntry = SupplierLedgerEntry.Create(
            command.ActiveShopId,
            supplierId,
            batch.Id,
            SupplierLedgerEntryType.CostAdjusted,
            delta,
            date,
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

        return await SaveAsync(command, source, lease, ledgerEntry.Value, adjustment.Value, cancellationToken);
    }

    private async Task<ErrorOr<ReceiptCostCorrectionResultDto>> SaveAsync(
        CorrectReceiptCostCommand command, LockedReceipt source, ShopLicenceLease lease,
        SupplierLedgerEntry ledgerEntry, PurchaseReceiptCostAdjustment adjustment, CancellationToken cancellationToken)
    {
        var (receipt, batch, _) = source;
        await writer.StageCorrectionAsync(command, receipt, batch, ledgerEntry, adjustment, cancellationToken);

        if (await CheckAuthorityAsync(command, cancellationToken) is { } authorityDenied)
            return authorityDenied;
        if (sourceReader.CheckCompletion(lease, receipt, adjustment.PerformedAt) is { } denied)
            return denied;

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (unitOfWork.IsConcurrencyConflict(ex))
        {
            return Errors.PurchaseReceipt.VersionConflict;
        }

        if (await CheckAuthorityAsync(command, cancellationToken) is { } postingAuthorityDenied)
            return postingAuthorityDenied;
        if (sourceReader.CheckCompletion(lease, receipt, adjustment.PerformedAt) is { } postingDenial)
            return postingDenial;

        return new ReceiptCostCorrectionResultDto(
            adjustment.Id,
            receipt.Id,
            batch.Id,
            adjustment.PriorTotalCost,
            adjustment.RevisedTotalCost,
            adjustment.Delta,
            ledgerEntry.Id,
            receipt.Version,
            adjustment.PerformedAt);
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
