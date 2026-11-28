using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Tills;
using Intelibill.Application.Common.Operations;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Application.Features.Sales.Services.Returns;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.RecordSaleReturn;

public sealed class RecordSaleReturnCommandHandler(
    ISaleReturnValidator saleReturnValidator,
    ISaleReturnNumberGenerator saleReturnNumberGenerator,
    SaleReturnCreditIssuer creditIssuer,
    SaleReturnStockPoster stockPoster,
    ISaleReturnRepository saleReturnRepository,
    IReturnAccountingPolicy returnAccountingPolicy,
    ISaleReturnMutationCoordinator mutationCoordinator,
    IUnitOfWork unitOfWork,
    IBusinessClock businessClock,
    IShopRepository shops, ISaleRepository sales, ITillPostingService tills, IFinancialOperationExecutor executor, Intelibill.Application.Features.Expiry.BatchDispositionService dispositions)
{
    [LicenceAction(LicenceAction.SourceCorrection)]
    public async Task<ErrorOr<Success>> HandleAsync(RecordSaleReturnCommand command, CancellationToken cancellationToken)
    {
        var member = await shops.GetMembershipAsync(command.ActorUserId, command.ShopId, cancellationToken);
        if (member is null || !member.IsActive) return Errors.Shop.MembershipNotFound;
        if (member.Role is not (ShopRole.Owner or ShopRole.Manager)) return Errors.Sale.ReturnForbidden;

        var key = FinancialOperationHasher.NormalizeKey(command.OperationKey);
        if (key is not null)
        {
            var result = await executor.ExecuteAsync<SaleReturnOperationOutcome>(new(command.ShopId, FinancialOperationType.SaleReturn, key)
                { SuspensionAction = LicenceAction.SourceCorrection },
                command.ActorUserId, command.SaleId, FinancialOperationHasher.Compute(command with { OperationKey = null }), async ct =>
                {
                    Guid? sourceId = null;
                    var posted = await ExecuteAsync(command, false, ct, id => sourceId = id);
                    if (posted.IsError) return posted.Errors;
                    return new SaleReturnOperationOutcome(sourceId ?? throw new InvalidOperationException("Return source was not recorded."), command.SaleId);
                }, cancellationToken);
            return result.IsError ? result.Errors : Result.Success;
        }
        return await ExecuteAsync(command, true, cancellationToken);
    }

    private Task<ErrorOr<Success>> ExecuteAsync(RecordSaleReturnCommand command, bool ownsTransaction, CancellationToken ct, Action<Guid>? recorded = null)
    {
        // This closure belongs to exactly one transaction attempt; never retain a locked session on the handler.
        TillSession? session = null;
        return mutationCoordinator.ExecuteAsync(command.ShopId,
            token => ResolveSaleAsync(command, ownsTransaction, token), async token =>
        {
            var gate = await tills.GateAsync(command.ShopId, token);
            if (gate.IsError) return gate.Errors;
            if (gate.Value && FinancialOperationHasher.NormalizeKey(command.OperationKey) is null)
                return Errors.Operation.InvalidKey;
            if (!gate.Value && (command.RefundTender is not null || command.RefundConfirmed.HasValue))
                return Errors.Till.Disabled;
            var shop = await shops.GetByIdAsync(command.ShopId, token);
            if (shop is null) return Errors.Shop.ShopNotFound;
            var acquired = await tills.AcquireAsync(command.ShopId, command.ActorUserId, command.Till,
                businessClock.GetBusinessDate(mutationCoordinator.PostingInstant ?? businessClock.UtcNow, shop.BusinessTimeZoneId), token);
            if (acquired.IsError) return acquired.Errors;
            session = acquired.Value;
            return Result.Success;
        }, (licence, token) => RecordAsync(command, session, licence, token, recorded), ownsTransaction, ct);
    }

    private async Task<ErrorOr<Guid>> ResolveSaleAsync(RecordSaleReturnCommand command, bool ownsTransaction, CancellationToken ct) =>
        ownsTransaction || await sales.ExistsAsync(command.ShopId, command.SaleId, ct)
            ? command.SaleId : Errors.Sale.NotFound(command.SaleId);

    private async Task<ErrorOr<Success>> RecordAsync(
        RecordSaleReturnCommand command,
        TillSession? session,
        ShopLicenceLease licence,
        CancellationToken cancellationToken,
        Action<Guid>? recorded = null)
    {
        var processedAt = mutationCoordinator.PostingInstant ?? businessClock.UtcNow;
        var validation = await saleReturnValidator.ValidateAsync(
            BuildValidationRequest(command, processedAt, licence),
            cancellationToken);

        if (validation.IsError)
            return validation.Errors;

        var validated = validation.Value;
        if (validated.Membership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Sale.ReturnForbidden;

        var warningValidation = ValidateWarnings(validated.Calculation.Warnings);
        if (warningValidation.IsError)
            return warningValidation.Errors;

        if (session is not null)
        {
            var refundValidation = ValidateRefundTender(command, validated.Calculation);
            if (refundValidation.IsError)
                return refundValidation.Errors;
        }

        // Classified before anything moves: a refund the policy cannot classify is refused whole.
        var classification = returnAccountingPolicy.Classify(validated.Calculation.Lines
            .Select(line => new ReturnAccountingLineRequest(
                line.SaleItemId,
                line.MaxRefundAmount,
                line.TaxAmount,
                line.ApprovedRefundAmount))
            .ToList());
        if (classification.IsError)
            return classification.Errors;

        var businessDay = businessClock.GetBusinessDay(processedAt, validated.Shop.BusinessTimeZoneId);
        var returnNumber = saleReturnNumberGenerator.Generate(businessDay.Date);

        // The batch lock precedes every inventory read.
        await dispositions.LockBatchesAsync(command.ShopId, validated.Lines.Where(x => x.Batch is not null && x.Disposition?.QuarantinedQuantity > 0).Select(x => x.Batch!.Id), cancellationToken);
        var restored = await stockPoster.RestoreStockAsync(command, validated, returnNumber, processedAt, cancellationToken);
        if (restored.IsError)
            return restored.Errors;

        var saleReturn = SaleReturn.Record(
            command.ShopId,
            validated.Sale.Id,
            returnNumber,
            processedAt,
            businessDay,
            command.ActorUserId,
            command.Notes,
            validated.Calculation.TotalRefundAmount,
            validated.Calculation.DueReductionAmount,
            validated.Calculation.PayoutAmount,
            command.PayoutDestination,
            validated.Calculation.TotalTaxableAmount,
            validated.Calculation.TotalTaxAmount,
            validated.Calculation.CustomerBalanceBefore,
            validated.Calculation.CustomerBalanceAfter,
            restored.Value.ReturnLines);

        if (saleReturn.IsError)
            return saleReturn.Errors;

        var staged = await stockPoster.StageRestorationsUnderBatchLockAsync(
            command,
            saleReturn.Value,
            restored.Value.Restorations,
            businessDay,
            processedAt,
            cancellationToken);
        if (staged.IsError)
            return staged.Errors;

        if (session is not null)
        {
            var evidence = saleReturn.Value.RecordRefundEvidence(command.RefundTender, command.RefundConfirmed == true);
            if (evidence.IsError) return evidence.Errors;
        }

        CustomerLedgerEntry? returnCredit = null;
        if (validated.Sale.CustomerId.HasValue && validated.Calculation.DueReductionAmount > 0m)
        {
            var returnCreditResult = SaleReturnCreditIssuer.CreateReturnCredit(command, validated, businessDay, returnNumber);
            if (returnCreditResult.IsError)
                return returnCreditResult.Errors;

            returnCredit = returnCreditResult.Value;
        }

        CreditNote? creditNote = null;
        if (command.PayoutDestination == ReturnPayoutDestination.CreditNote
            && validated.Calculation.PayoutAmount > 0m)
        {
            var creditNoteResult = await creditIssuer.IssueCreditNoteAsync(
                command,
                validated,
                saleReturn.Value,
                businessDay,
                returnNumber,
                cancellationToken);
            if (creditNoteResult.IsError)
                return creditNoteResult.Errors;

            creditNote = creditNoteResult.Value;
        }

        var snapshot = ReturnAccountingSnapshot.Create(
            returnAccountingPolicy.Version,
            classification.Value,
            BuildSettlements(validated.Sale, validated.Calculation, command.PayoutDestination, returnCredit, creditNote));
        if (snapshot.IsError)
            return snapshot.Errors;

        var posting = saleReturn.Value.PostAccounting(snapshot.Value);
        if (posting.IsError)
            return posting.Errors;

        await saleReturnRepository.AddAsync(saleReturn.Value, cancellationToken);
        await creditIssuer.AddCreditsAsync(returnCredit, creditNote, cancellationToken);

        if (session is not null)
        {
            var appended = await tills.AppendAsync(session, new(TillMovementSource.SaleReturn, saleReturn.Value.Id, "refund",
                command.RefundTender is null ? [] : [command.RefundTender], -1m, processedAt, command.OperationKey!.Trim(), command.Notes,
                StoreCreditIssued: command.PayoutDestination == ReturnPayoutDestination.CreditNote ? validated.Calculation.PayoutAmount : 0m,
                ReceivableChange: -validated.Calculation.DueReductionAmount), command.ActorUserId, cancellationToken);
            if (appended.IsError) return appended.Errors;
        }

        return await SaveGuardedAsync(command, licence, validated.Sale, saleReturn.Value.Id, recorded, cancellationToken);
    }

    private static SaleReturnValidationRequest BuildValidationRequest(
        RecordSaleReturnCommand command,
        DateTimeOffset processedAt,
        ShopLicenceLease licence)
    {
        return new SaleReturnValidationRequest(
            command.ActorUserId,
            command.ShopId,
            command.SaleId,
            command.DueReductionOverrideAmount,
            command.DueOverrideReason,
            command.Items.Select(i => new SaleReturnValidationLineRequest(
                i.SaleItemId,
                i.Quantity,
                i.LineType,
                i.Condition,
                i.ApprovedRefundAmount,
                i.Notes)).ToList(), processedAt, licence);
    }

    private static ErrorOr<Success> ValidateRefundTender(
        RecordSaleReturnCommand command,
        SaleReturnCalculationResult calculation)
    {
        var external = command.PayoutDestination == ReturnPayoutDestination.Refund ? calculation.PayoutAmount : 0m;
        if (external > 0 && command.RefundTender is null) return Errors.Till.TenderRequired;
        if ((command.RefundConfirmed == true) != (external > 0))
            return Error.Validation("Till.RefundConfirmationInvalid", "Confirm the actual external refund only when an external payout occurs.");
        var tenderValidation = TenderLine.ValidateLines(command.RefundTender is null ? [] : [command.RefundTender], external);
        if (tenderValidation.IsError) return tenderValidation.Errors;

        return Result.Success;
    }

    /// <summary>
    /// Re-checks the actor's role and the source policy around the single save, then reports the new return id.
    /// </summary>
    private async Task<ErrorOr<Success>> SaveGuardedAsync(
        RecordSaleReturnCommand command,
        ShopLicenceLease licence,
        Sale sale,
        Guid saleReturnId,
        Action<Guid>? recorded,
        CancellationToken cancellationToken)
    {
        var currentRole = await shops.GetActiveRoleAsync(command.ActorUserId, command.ShopId, cancellationToken);
        if (currentRole is null) return Errors.Shop.MembershipNotFound;
        if (currentRole is not (ShopRole.Owner or ShopRole.Manager)) return Errors.Sale.ReturnForbidden;
        if (SaleReturnSourcePolicy.Check(licence, sale, businessClock) is { } denied) return denied;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (SaleReturnSourcePolicy.Check(licence, sale, businessClock) is { } saveDenied) return saveDenied;
        recorded?.Invoke(saleReturnId);

        return Result.Success;
    }

    /// <summary>
    /// The settlement legs exactly as the return moved money: the due reduction through the
    /// customer's ledger entry, the payout as the credit note issued or as a refund outside the books.
    /// </summary>
    private static List<ReturnSettlementInput> BuildSettlements(
        Sale sale,
        SaleReturnCalculationResult calculation,
        ReturnPayoutDestination? payoutDestination,
        CustomerLedgerEntry? returnCredit,
        CreditNote? creditNote)
    {
        var settlements = new List<ReturnSettlementInput>(2);
        if (calculation.DueReductionAmount > 0m)
        {
            settlements.Add(new ReturnSettlementInput(
                ReturnSettlementKind.ReceivableReduction,
                calculation.DueReductionAmount,
                sale.CustomerId,
                CustomerLedgerEntryId: returnCredit?.Id));
        }

        if (calculation.PayoutAmount > 0m)
        {
            settlements.Add(payoutDestination == ReturnPayoutDestination.CreditNote && creditNote is not null
                ? new ReturnSettlementInput(ReturnSettlementKind.StoreCreditIssued, calculation.PayoutAmount, sale.CustomerId, creditNote.Id)
                : new ReturnSettlementInput(ReturnSettlementKind.ExternalRefund, calculation.PayoutAmount, sale.CustomerId));
        }

        return settlements;
    }

    private static ErrorOr<Success> ValidateWarnings(
        IReadOnlyList<SaleReturnCalculationWarning> warnings)
    {
        var errors = warnings
            .Where(warning =>
                warning.Code == "sale_return.note_required.due_override"
                || warning.Code == "sale_return.due_override_exceeds_outstanding"
                || warning.Code == "sale_return.note_required.refund_override")
            .Select(warning => warning.Code switch
            {
                "sale_return.note_required.due_override" => Errors.Sale.ReturnDueOverrideReasonRequired,
                "sale_return.due_override_exceeds_outstanding" => Errors.Sale.ReturnDueReductionExceedsOutstandingDue,
                "sale_return.note_required.refund_override" => Errors.Sale.ReturnRefundOverrideReasonRequired,
                _ => Errors.Sale.ReturnDueOverrideReasonRequired,
            })
            .ToList();

        return errors.Count > 0
            ? errors
            : Result.Success;
    }
}
