using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Tills;
using Intelibill.Application.Features.Tills.Services;
using Intelibill.Application.Common.Operations;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Services.Returns;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.VoidSaleReturn;

public sealed class VoidSaleReturnCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISaleRepository saleRepository,
    ISaleReturnRepository saleReturnRepository,
    SaleReturnStockPoster stockPoster,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository,
    ICreditNoteRepository creditNoteRepository,
    ISaleReturnMutationCoordinator mutationCoordinator,
    IUnitOfWork unitOfWork,
    IBusinessClock businessClock, TillSourceSettlementService settlement, IFinancialOperationExecutor executor, Intelibill.Application.Features.Expiry.BatchDispositionService dispositions)
{
    [LicenceAction(LicenceAction.SourceCorrection)]
    public async Task<ErrorOr<Success>> HandleAsync(
        VoidSaleReturnCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
            return Errors.Sale.ReturnVoidReasonRequired;

        var user = await userRepository.GetByIdAsync(command.ActorUserId, cancellationToken);
        if (user is null)
            return Errors.User.NotFound;

        var shop = await shopRepository.GetByIdAsync(command.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(command.ActorUserId, command.ShopId, cancellationToken);
        if (membership is null || !membership.IsActive)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Sale.ReturnForbidden;

        var key = FinancialOperationHasher.NormalizeKey(command.OperationKey);
        var result = key is null
            ? await ExecuteAsync(command, shop, true, cancellationToken)
            : await executor.ExecuteAsync<SaleReturnOperationOutcome>(new(command.ShopId, FinancialOperationType.SaleReturnVoid, key)
                { SuspensionAction = LicenceAction.SourceCorrection },
                command.ActorUserId, command.SaleReturnId, FinancialOperationHasher.Compute(command with { OperationKey = null }),
                token => ExecuteAsync(command, shop, false, token), cancellationToken);
        return result.IsError ? result.Errors : Result.Success;
    }

    private Task<ErrorOr<SaleReturnOperationOutcome>> ExecuteAsync(VoidSaleReturnCommand command, Shop shop, bool ownsTransaction, CancellationToken ct)
    {
        TillSession? session = null;
        return mutationCoordinator.ExecuteAsync(command.ShopId, token => ResolveSaleAsync(command, token), async token =>
        {
            if (command.Till is not null && command.SettlementEvidence?.Disposition == TillSettlementDisposition.NoPhysicalMovement)
                return Error.Validation("Till.SettlementEvidenceInvalid", "No physical movement must not select a drawer.");
            var acquired = await settlement.AcquireAsync(command.ShopId, command.ActorUserId, command.Till, command.OperationKey,
                command.SettlementEvidence, command.SettlementEvidence?.Tender?.AppliedAmount ?? 0m,
                businessClock.GetBusinessDate(mutationCoordinator.PostingInstant ?? businessClock.UtcNow, shop.BusinessTimeZoneId), token);
            if (acquired.IsError) return acquired.Errors;
            session = acquired.Value;
            return Result.Success;
        }, (licence, token) => VoidAsync(command, shop, session, licence, token), ownsTransaction, ct);
    }

    private async Task<ErrorOr<Guid>> ResolveSaleAsync(VoidSaleReturnCommand command, CancellationToken ct)
    {
        // Only the immutable parent id is read before its lock. The return is reloaded under that lock.
        var id = await saleReturnRepository.GetSaleIdAsync(command.ShopId, command.SaleReturnId, ct);
        return id is { } saleId ? saleId : Errors.Sale.ReturnNotFound(command.SaleReturnId);
    }

    private async Task<ErrorOr<SaleReturnOperationOutcome>> VoidAsync(
        VoidSaleReturnCommand command,
        Shop shop,
        TillSession? session,
        ShopLicenceLease licence,
        CancellationToken cancellationToken)
    {
        var source = await LoadSourceAsync(command, licence, cancellationToken);
        if (source.IsError)
            return source.Errors;

        var (saleReturn, sale) = source.Value;

        // Validate the source's immutable payout evidence before stock, credit or accounting changes.
        if (command.SettlementEvidence is { } evidence)
        {
            var recorded = saleReturn.RecordVoidSettlementEvidence(evidence);
            if (recorded.IsError) return recorded.Errors;
        }

        // Locked so a redemption cannot land between this check and the void.
        var creditNote = await creditNoteRepository.GetBySaleReturnIdForUpdateWithRedemptionsAsync(
            command.ShopId, command.SaleReturnId, cancellationToken);

        if (creditNote is not null && creditNote.HasBeenRedeemed)
            return Errors.Sale.ReturnCreditNoteHasRedemptions;

        var saleItemsById = sale.Items.ToDictionary(item => item.Id);
        var processedAt = mutationCoordinator.PostingInstant ?? businessClock.UtcNow;
        // The void lands on the day it happened in the shop's current zone, the one the coordinator
        // fenced the period with.
        var voidDate = businessClock.GetBusinessDate(processedAt, shop.BusinessTimeZoneId);

        // The batch lock precedes every inventory read.
        await dispositions.LockBatchesAsync(command.ShopId, saleReturn.Items.Where(x => x.QuarantineChangeId.HasValue && saleItemsById.ContainsKey(x.SaleItemId))
            .Select(x => saleItemsById[x.SaleItemId]).Where(x => x.InventoryBatchId.HasValue).Select(x => x.InventoryBatchId!.Value), cancellationToken);
        var stockReversals = await stockPoster.ReverseStockUnderBatchLockAsync(
            command,
            saleReturn,
            saleItemsById,
            processedAt,
            voidDate,
            cancellationToken);
        if (stockReversals.IsError)
            return stockReversals.Errors;

        CustomerLedgerEntry? returnCreditReversal = null;
        if (saleReturn.DueReductionAmount > 0m && sale.CustomerId.HasValue)
        {
            var ledgerResult = CreateReturnCreditReversal(command, saleReturn, sale, voidDate);
            if (ledgerResult.IsError)
                return ledgerResult.Errors;

            returnCreditReversal = ledgerResult.Value;
        }

        var voided = VoidSourceAndCreditNote(command, saleReturn, creditNote, processedAt, voidDate);
        if (voided.IsError)
            return voided.Errors;

        await stockPoster.AddReversalsAsync(stockReversals.Value, cancellationToken);

        if (returnCreditReversal is not null)
            await customerLedgerEntryRepository.AddAsync(returnCreditReversal, cancellationToken);

        var appended = await settlement.AppendAsync(session, TillMovementSource.SaleReturnVoid, saleReturn.Id,
            command.ActorUserId, command.OperationKey, command.SettlementEvidence, cancellationToken);
        if (appended.IsError) return appended.Errors;
        saleReturnRepository.Update(saleReturn);
        return await SaveGuardedAsync(command, licence, sale, saleReturn, cancellationToken);
    }

    private async Task<ErrorOr<(SaleReturn SaleReturn, Sale Sale)>> LoadSourceAsync(
        VoidSaleReturnCommand command,
        ShopLicenceLease licence,
        CancellationToken cancellationToken)
    {
        var saleReturn = await saleReturnRepository.GetByIdWithItemsAsync(
            command.ShopId,
            command.SaleReturnId,
            cancellationToken);
        if (saleReturn is null)
            return Errors.Sale.ReturnNotFound(command.SaleReturnId);

        if (saleReturn.IsVoided)
            return Errors.Sale.ReturnAlreadyVoided;

        var sale = await saleRepository.GetByIdAsync(saleReturn.SaleId, command.ShopId, cancellationToken);
        if (sale is null)
            return Errors.Sale.NotFound(saleReturn.SaleId);

        if (SaleReturnSourcePolicy.Check(licence, sale, businessClock, saleReturn) is { } denied) return denied;

        return (saleReturn, sale);
    }

    private static ErrorOr<CustomerLedgerEntry> CreateReturnCreditReversal(
        VoidSaleReturnCommand command,
        SaleReturn saleReturn,
        Sale sale,
        DateOnly voidDate)
    {
        return CustomerLedgerEntry.Create(
            command.ShopId,
            sale.CustomerId!.Value,
            sale.Id,
            CustomerLedgerEntryType.ReturnCreditReversal,
            saleReturn.DueReductionAmount,
            voidDate,
            $"Void return credit from {saleReturn.ReturnNumber} for sale {sale.InvoiceNumber}",
            command.ActorUserId);
    }

    private ErrorOr<Success> VoidSourceAndCreditNote(
        VoidSaleReturnCommand command,
        SaleReturn saleReturn,
        CreditNote? creditNote,
        DateTimeOffset processedAt,
        DateOnly voidDate)
    {
        var voidResult = saleReturn.Void(processedAt, voidDate, command.ActorUserId, command.Reason);
        if (voidResult.IsError)
            return voidResult.Errors;

        if (creditNote is not null && !creditNote.IsVoided)
        {
            var noteVoidResult = creditNote.Void(command.Reason);
            if (noteVoidResult.IsError)
                return noteVoidResult.Errors;
            creditNoteRepository.Update(creditNote);
        }

        return Result.Success;
    }

    private async Task<ErrorOr<SaleReturnOperationOutcome>> SaveGuardedAsync(
        VoidSaleReturnCommand command,
        ShopLicenceLease licence,
        Sale sale,
        SaleReturn saleReturn,
        CancellationToken cancellationToken)
    {
        var currentRole = await shopRepository.GetActiveRoleAsync(command.ActorUserId, command.ShopId, cancellationToken);
        if (currentRole is null) return Errors.Shop.MembershipNotFound;
        if (currentRole is not (ShopRole.Owner or ShopRole.Manager)) return Errors.Sale.ReturnForbidden;
        if (SaleReturnSourcePolicy.Check(licence, sale, businessClock, saleReturn) is { } writeDenied) return writeDenied;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return SaleReturnSourcePolicy.Check(licence, sale, businessClock, saleReturn) is { } saveDenied
            ? saveDenied : new SaleReturnOperationOutcome(saleReturn.Id, sale.Id);
    }
}
