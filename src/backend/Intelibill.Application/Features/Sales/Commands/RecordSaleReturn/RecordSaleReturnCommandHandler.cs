using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Application.Features.Sales.Services.Returns;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.RecordSaleReturn;

public sealed class RecordSaleReturnCommandHandler(
    ISaleReturnValidator saleReturnValidator,
    ISaleReturnNumberGenerator saleReturnNumberGenerator,
    IInventoryRepository inventoryRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IStockTransactionRepository stockTransactionRepository,
    ISaleReturnRepository saleReturnRepository,
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

        if (validated.Calculation.DueReductionAmount > 0m)
            return Errors.Sale.ReturnCustomerDueNotSupported;

        var noteValidation = ValidateRequiredNotes(validated.Calculation.Warnings);
        if (noteValidation.IsError)
            return noteValidation.Errors;

        var payoutValidation = ValidatePayoutMethod(validated.Calculation.PayoutAmount, command.PayoutMethod);
        if (payoutValidation.IsError)
            return payoutValidation.Errors;

        var processedAt = DateTimeOffset.UtcNow;
        var returnNumber = saleReturnNumberGenerator.Generate(processedAt);
        var calculationBySaleItemId = validated.Calculation.Lines.ToDictionary(line => line.SaleItemId);
        var returnItems = new List<SaleReturnItem>();
        var restocks = new List<PreparedRestock>();

        foreach (var line in validated.Lines)
        {
            var calculated = calculationBySaleItemId[line.Request.SaleItemId];
            var returnItem = SaleReturnItem.Create(
                command.ShopId,
                validated.Sale.Id,
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
                calculated.Notes);

            if (returnItem.IsError)
                return returnItem.Errors;

            returnItems.Add(returnItem.Value);

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

            restocks.Add(new PreparedRestock(line, inventory, transaction.Value));
        }

        var saleReturn = SaleReturn.Create(
            command.ShopId,
            validated.Sale.Id,
            returnNumber,
            processedAt,
            command.ActorUserId,
            command.Notes,
            validated.Calculation.TotalRefundAmount,
            validated.Calculation.DueReductionAmount,
            validated.Calculation.PayoutAmount,
            validated.Calculation.TotalTaxableAmount,
            validated.Calculation.TotalTaxAmount,
            validated.Calculation.CustomerBalanceBefore,
            validated.Calculation.CustomerBalanceAfter,
            returnItems);

        if (saleReturn.IsError)
            return saleReturn.Errors;

        foreach (var restock in restocks)
        {
            var batchResult = restock.Line.Batch.AddQuantity(restock.Line.Request.Quantity, command.ActorUserId);
            if (batchResult.IsError)
                return batchResult.Errors;

            var inventoryResult = restock.Inventory.AddQuantity(restock.Line.Request.Quantity, command.ActorUserId);
            if (inventoryResult.IsError)
                return inventoryResult.Errors;

            inventoryBatchRepository.Update(restock.Line.Batch);
            inventoryRepository.Update(restock.Inventory);
            await stockTransactionRepository.AddAsync(restock.StockTransaction, cancellationToken);
        }

        await saleReturnRepository.AddAsync(saleReturn.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }

    private static ErrorOr<Success> ValidatePayoutMethod(decimal payoutAmount, PaymentMethod? payoutMethod)
    {
        if (payoutAmount <= 0m)
            return Result.Success;

        if (!payoutMethod.HasValue)
            return Errors.Sale.ReturnPayoutMethodRequired;

        return payoutMethod.Value is PaymentMethod.Cash or PaymentMethod.UPI or PaymentMethod.Card
            ? Result.Success
            : Errors.Sale.ReturnPayoutMethodInvalid;
    }

    private static ErrorOr<Success> ValidateRequiredNotes(
        IReadOnlyList<SaleReturnCalculationWarning> warnings)
    {
        var errors = warnings
            .Where(warning => warning.Code.StartsWith("sale_return.note_required.", StringComparison.Ordinal))
            .Select(warning => warning.Code switch
            {
                "sale_return.note_required.wastage" => Errors.Sale.ReturnNoteRequired("wastage returns"),
                "sale_return.note_required.partial_refund" => Errors.Sale.ReturnNoteRequired("partial refunds"),
                "sale_return.note_required.zero_refund" => Errors.Sale.ReturnNoteRequired("zero refunds"),
                _ => Errors.Sale.ReturnNoteRequired("this return"),
            })
            .ToList();

        return errors.Count > 0
            ? errors
            : Result.Success;
    }

    private sealed record PreparedRestock(
        ValidatedSaleReturnLine Line,
        Intelibill.Domain.Entities.Inventory Inventory,
        StockTransaction StockTransaction);
}
