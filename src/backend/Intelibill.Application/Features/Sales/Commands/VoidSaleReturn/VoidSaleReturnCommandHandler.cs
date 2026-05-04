using ErrorOr;
using Intelibill.Application.Common.Errors;
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
    IInventoryRepository inventoryRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IStockTransactionRepository stockTransactionRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<Success>> HandleAsync(
        VoidSaleReturnCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
            return Errors.Sale.ReturnVoidReasonRequired;

        var user = await userRepository.GetByIdAsync(command.ActorUserId, cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", "User not found.");

        var shop = await shopRepository.GetByIdAsync(command.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(command.ActorUserId, command.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Sale.ReturnForbidden;

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

        var saleItemsById = sale.Items.ToDictionary(item => item.Id);
        var processedAt = DateTimeOffset.UtcNow;
        var stockReversals = new List<StockTransaction>();

        foreach (var returnItem in saleReturn.Items.Where(item => item.Condition == SaleReturnCondition.Restockable))
        {
            if (!saleItemsById.TryGetValue(returnItem.SaleItemId, out var saleItem))
                return Errors.Sale.ReturnSaleItemNotFound(returnItem.SaleItemId);

            var batch = await inventoryBatchRepository.GetByIdAsync(saleItem.InventoryBatchId, cancellationToken);
            if (batch is null || batch.ShopId != command.ShopId)
                return Errors.Sale.ReturnBatchNotFound(saleItem.InventoryBatchId);

            var inventory = await inventoryRepository.GetByItemAsync(command.ShopId, saleItem.ItemId, cancellationToken);
            if (inventory is null)
                return Errors.Sale.ReturnInventoryAggregateNotFound(saleItem.ItemId);

            if (batch.Quantity < returnItem.Quantity || inventory.Quantity < returnItem.Quantity)
                return Errors.Sale.ReturnVoidInsufficientStock;

            var batchResult = batch.SubtractQuantity(returnItem.Quantity, command.ActorUserId);
            if (batchResult.IsError)
                return batchResult.Errors;

            var inventoryResult = inventory.SubtractQuantity(returnItem.Quantity, command.ActorUserId);
            if (inventoryResult.IsError)
                return inventoryResult.Errors;

            var stockTransaction = StockTransaction.Create(
                command.ShopId,
                saleItem.ItemId,
                saleItem.InventoryBatchId,
                StockTransactionType.Out,
                -returnItem.Quantity,
                saleReturn.ReturnNumber,
                command.Reason,
                processedAt,
                command.ActorUserId,
                command.ActorUserId);
            if (stockTransaction.IsError)
                return stockTransaction.Errors;

            inventoryBatchRepository.Update(batch);
            inventoryRepository.Update(inventory);
            stockReversals.Add(stockTransaction.Value);
        }

        CustomerLedgerEntry? returnCreditReversal = null;
        if (saleReturn.DueReductionAmount > 0m && sale.CustomerId.HasValue)
        {
            var ledgerResult = CustomerLedgerEntry.Create(
                command.ShopId,
                sale.CustomerId.Value,
                sale.Id,
                CustomerLedgerEntryType.ReturnCreditReversal,
                saleReturn.DueReductionAmount,
                DateOnly.FromDateTime(processedAt.UtcDateTime),
                $"Void return credit from {saleReturn.ReturnNumber} for sale {sale.InvoiceNumber}",
                command.ActorUserId);
            if (ledgerResult.IsError)
                return ledgerResult.Errors;

            returnCreditReversal = ledgerResult.Value;
        }

        var voidResult = saleReturn.Void(processedAt, command.ActorUserId, command.Reason);
        if (voidResult.IsError)
            return voidResult.Errors;

        foreach (var stockReversal in stockReversals)
            await stockTransactionRepository.AddAsync(stockReversal, cancellationToken);

        if (returnCreditReversal is not null)
            await customerLedgerEntryRepository.AddAsync(returnCreditReversal, cancellationToken);

        saleReturnRepository.Update(saleReturn);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
