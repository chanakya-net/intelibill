using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.CreditNotes.Services;
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
    ICreditNoteCodeGenerator creditNoteCodeGenerator,
    ICreditNoteRepository creditNoteRepository,
    IInventoryRepository inventoryRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IStockTransactionRepository stockTransactionRepository,
    ISaleReturnRepository saleReturnRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository,
    IReturnAccountingPolicy returnAccountingPolicy,
    ISaleReturnMutationCoordinator mutationCoordinator,
    IUnitOfWork unitOfWork,
    IBusinessClock businessClock)
{
    public Task<ErrorOr<Success>> HandleAsync(
        RecordSaleReturnCommand command,
        CancellationToken cancellationToken) =>
        // Validation runs under the sale lock: what a competing return committed is counted, and
        // nothing can be returned between the check and the write.
        mutationCoordinator.ExecuteAsync(
            command.ShopId,
            command.SaleId,
            ct => RecordAsync(command, ct),
            cancellationToken);

    private async Task<ErrorOr<Success>> RecordAsync(
        RecordSaleReturnCommand command,
        CancellationToken cancellationToken)
    {
        var validation = await saleReturnValidator.ValidateAsync(
            new SaleReturnValidationRequest(
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
                    i.Notes)).ToList()),
            cancellationToken);

        if (validation.IsError)
            return validation.Errors;

        var validated = validation.Value;
        if (validated.Membership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Sale.ReturnForbidden;

        var warningValidation = ValidateWarnings(validated.Calculation.Warnings);
        if (warningValidation.IsError)
            return warningValidation.Errors;

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

        var processedAt = businessClock.UtcNow;
        var businessDay = businessClock.GetBusinessDay(processedAt, validated.Shop.BusinessTimeZoneId);
        var returnNumber = saleReturnNumberGenerator.Generate(businessDay.Date);
        var calculationBySaleItemId = validated.Calculation.Lines.ToDictionary(line => line.SaleItemId);
        var returnLines = new List<SaleReturnLineInput>();

        foreach (var line in validated.Lines)
        {
            var calculated = calculationBySaleItemId[line.Request.SaleItemId];
            returnLines.Add(new SaleReturnLineInput(
                command.ShopId,
                line.SaleItem.Id,
                calculated.Quantity,
                calculated.Condition,
                calculated.OriginalCostPrice,
                calculated.OriginalSalesPrice,
                calculated.OriginalTaxRatePercent,
                calculated.OriginalIsPriceIncludingTax,
                calculated.MaxRefundAmount,
                calculated.ApprovedRefundAmount,
                calculated.TaxableAmount,
                calculated.TaxAmount,
                calculated.Notes));

            if (line.SaleItem.LineType == SaleLineType.Service || line.Request.Condition == SaleReturnCondition.Wastage)
                continue;

            if (!line.SaleItem.ItemId.HasValue || !line.SaleItem.InventoryBatchId.HasValue)
                return Errors.Sale.ReturnSaleItemNotFound(line.SaleItem.Id);

            var itemId = line.SaleItem.ItemId.Value;
            var batchId = line.SaleItem.InventoryBatchId.Value;
            var batch = line.Batch;
            if (batch is null)
                return Errors.Sale.ReturnBatchNotFound(batchId);

            var inventory = await inventoryRepository.GetByItemAsync(command.ShopId, itemId, cancellationToken);
            if (inventory is null)
                return Errors.Sale.ReturnInventoryAggregateNotFound(itemId);

            var transaction = StockTransaction.Create(
                command.ShopId,
                itemId,
                batchId,
                StockTransactionType.Ret,
                line.Request.Quantity,
                returnNumber,
                line.Request.Notes,
                processedAt,
                command.ActorUserId,
                command.ActorUserId);

            if (transaction.IsError)
                return transaction.Errors;

            var batchResult = batch.AddQuantity(line.Request.Quantity, command.ActorUserId);
            if (batchResult.IsError)
                return batchResult.Errors;

            var inventoryResult = inventory.AddQuantity(line.Request.Quantity, command.ActorUserId);
            if (inventoryResult.IsError)
                return inventoryResult.Errors;

            inventoryBatchRepository.Update(batch);
            inventoryRepository.Update(inventory);
            await stockTransactionRepository.AddAsync(transaction.Value, cancellationToken);
        }

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
            returnLines);

        if (saleReturn.IsError)
            return saleReturn.Errors;

        CustomerLedgerEntry? returnCredit = null;
        if (validated.Sale.CustomerId.HasValue && validated.Calculation.DueReductionAmount > 0m)
        {
            var returnCreditResult = CustomerLedgerEntry.Create(
                command.ShopId,
                validated.Sale.CustomerId.Value,
                validated.Sale.Id,
                CustomerLedgerEntryType.ReturnCredit,
                validated.Calculation.DueReductionAmount,
                businessDay.Date,
                $"Return credit from {returnNumber} for sale {validated.Sale.InvoiceNumber}",
                command.ActorUserId);

            if (returnCreditResult.IsError)
                return returnCreditResult.Errors;

            returnCredit = returnCreditResult.Value;
        }

        CreditNote? creditNote = null;
        if (command.PayoutDestination == ReturnPayoutDestination.CreditNote
            && validated.Calculation.PayoutAmount > 0m)
        {
            var code = await creditNoteCodeGenerator.GenerateAsync(command.ShopId, businessDay.Date, cancellationToken);
            var reason = ResolveCreditNoteReason(command, returnNumber);
            var creditNoteResult = CreditNote.Issue(
                command.ShopId,
                saleReturn.Value.Id,
                validated.Calculation.PayoutAmount,
                reason,
                code,
                command.CreditNoteExpiresAt,
                validated.Sale.CustomerId);

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
        if (returnCredit is not null)
            await customerLedgerEntryRepository.AddAsync(returnCredit, cancellationToken);
        if (creditNote is not null)
            await creditNoteRepository.AddAsync(creditNote, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

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

    private static string ResolveCreditNoteReason(RecordSaleReturnCommand command, string returnNumber)
    {
        if (!string.IsNullOrWhiteSpace(command.CreditNoteReason))
        {
            return command.CreditNoteReason.Trim();
        }

        if (!string.IsNullOrWhiteSpace(command.Notes))
        {
            return command.Notes.Trim();
        }

        return $"Return credit from {returnNumber}";
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
