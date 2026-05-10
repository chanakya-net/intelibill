using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Application.Features.Sales.Services.Returns;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.RecordSaleReturn;

public sealed class RecordSaleReturnCommandHandler(
    ISaleReturnValidator saleReturnValidator,
    ISaleReturnNumberGenerator saleReturnNumberGenerator,
    IInventoryRepository inventoryRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IStockTransactionRepository stockTransactionRepository,
    ISaleReturnRepository saleReturnRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<Success>> HandleAsync(
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

        var processedAt = DateTimeOffset.UtcNow;
        var returnNumber = saleReturnNumberGenerator.Generate(processedAt);
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

            if (line.Request.Condition == SaleReturnCondition.Wastage)
                continue;

            var inventory = await inventoryRepository.GetByItemAsync(command.ShopId, line.SaleItem.ItemId, cancellationToken);
            if (inventory is null)
                return Errors.Sale.ReturnInventoryAggregateNotFound(line.SaleItem.ItemId);

            var transaction = StockTransaction.Create(
                command.ShopId,
                line.SaleItem.ItemId,
                line.SaleItem.InventoryBatchId,
                StockTransactionType.Ret,
                line.Request.Quantity,
                returnNumber,
                line.Request.Notes,
                processedAt,
                command.ActorUserId,
                command.ActorUserId);

            if (transaction.IsError)
                return transaction.Errors;

            var batchResult = line.Batch.AddQuantity(line.Request.Quantity, command.ActorUserId);
            if (batchResult.IsError)
                return batchResult.Errors;

            var inventoryResult = inventory.AddQuantity(line.Request.Quantity, command.ActorUserId);
            if (inventoryResult.IsError)
                return inventoryResult.Errors;

            inventoryBatchRepository.Update(line.Batch);
            inventoryRepository.Update(inventory);
            await stockTransactionRepository.AddAsync(transaction.Value, cancellationToken);
        }

        var saleReturn = SaleReturn.Record(
            command.ShopId,
            validated.Sale.Id,
            returnNumber,
            processedAt,
            command.ActorUserId,
            command.Notes,
            validated.Calculation.TotalRefundAmount,
            validated.Calculation.DueReductionAmount,
            validated.Calculation.PayoutAmount,
            command.PayoutMethod,
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
                DateOnly.FromDateTime(processedAt.UtcDateTime),
                $"Return credit from {returnNumber} for sale {validated.Sale.InvoiceNumber}",
                command.ActorUserId);

            if (returnCreditResult.IsError)
                return returnCreditResult.Errors;

            returnCredit = returnCreditResult.Value;
        }

        await saleReturnRepository.AddAsync(saleReturn.Value, cancellationToken);
        if (returnCredit is not null)
            await customerLedgerEntryRepository.AddAsync(returnCredit, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
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
