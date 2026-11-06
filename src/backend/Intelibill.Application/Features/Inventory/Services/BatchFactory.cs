using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.AddInventoryBatch;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Services;

internal sealed class BatchFactory(
    IInventoryBatchRepository inventoryBatchRepository,
    IStockTransactionRepository stockTransactionRepository,
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository,
    IPurchaseReceiptValueRepository purchaseReceiptValueRepository,
    IShopRepository shopRepository,
    IBusinessClock businessClock) : IBatchFactory
{
    public async Task<ErrorOr<BatchCreationResult>> CreateBatchAsync(
        Guid shopId,
        Guid itemId,
        InboundInventoryLineInput row,
        Supplier supplier,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var normalizedBatchNumber = row.BatchNumber.Trim();

        var existingBatch = await inventoryBatchRepository.GetByBatchNumberAsync(
            shopId, itemId, normalizedBatchNumber, cancellationToken);

        if (existingBatch is not null && !existingBatch.IsVoided)
            return Errors.Inventory.BatchNumberAlreadyExists;

        var shop = await shopRepository.GetByIdAsync(shopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var isSystemSupplier = row.SupplierId is null;

        var batchResult = InventoryBatch.Create(
            shopId,
            itemId,
            normalizedBatchNumber,
            row.Quantity,
            ComputeUnitCost(row.TotalPurchaseCost, row.Quantity),
            row.Mrp,
            row.SalesPrice,
            row.TaxRatePercent,
            row.TaxIncluded,
            row.ExpiryDate,
            row.ManufacturingDate,
            supplier.Id,
            actorUserId,
            row.PurchaseTaxIncluded);

        if (batchResult.IsError)
            return batchResult.Errors;

        var batch = batchResult.Value;
        await inventoryBatchRepository.AddAsync(batch, cancellationToken);

        var performedAt = (row.PerformedAt ?? businessClock.UtcNow).ToUniversalTime();

        var stockTransactionResult = StockTransaction.Create(
            shopId,
            itemId,
            batch.Id,
            StockTransactionType.In,
            row.Quantity,
            row.ReferenceNumber,
            row.Notes,
            performedAt,
            actorUserId,
            actorUserId);

        if (stockTransactionResult.IsError)
            return stockTransactionResult.Errors;

        var stockTransaction = stockTransactionResult.Value;
        await stockTransactionRepository.AddAsync(stockTransaction, cancellationToken);

        var ledgerAmount = decimal.Round(row.TotalPurchaseCost, 2, MidpointRounding.AwayFromZero);

        // The total is kept as entered (to the paisa), so later corrections and voids work from what
        // the payable was posted with rather than from the rounded unit cost.
        var receiptValueResult = PurchaseReceiptValue.Create(
            shopId,
            batch.Id,
            itemId,
            supplier.Id,
            row.Quantity,
            ledgerAmount,
            row.ReferenceNumber,
            performedAt,
            actorUserId);

        if (receiptValueResult.IsError)
            return receiptValueResult.Errors;

        var receiptValue = receiptValueResult.Value;
        await purchaseReceiptValueRepository.AddAsync(receiptValue, cancellationToken);

        // A free receipt (sample, bonus stock) owes the supplier nothing: no zero-value payable.
        if (ledgerAmount == 0)
            return new BatchCreationResult(batch, stockTransaction, null, receiptValue);

        var ledgerResult = SupplierLedgerEntry.Create(
            shopId,
            supplier.Id,
            batch.Id,
            SupplierLedgerEntryType.GoodsReceived,
            ledgerAmount,
            businessClock.GetBusinessDate(performedAt, shop.BusinessTimeZoneId),
            isSystemSupplier ? "Receipt with no supplier assigned" : null,
            actorUserId);

        if (ledgerResult.IsError)
            return Errors.Inventory.SupplierLedgerEntryInvalid;

        var ledgerEntry = ledgerResult.Value;
        await supplierLedgerEntryRepository.AddAsync(ledgerEntry, cancellationToken);

        return new BatchCreationResult(batch, stockTransaction, ledgerEntry, receiptValue);
    }

    public Task<ErrorOr<BatchCreationResult>> CreateBatchAsync(
        Guid shopId,
        Guid itemId,
        AddInventoryBatchRowCommand row,
        Supplier supplier,
        Guid actorUserId,
        CancellationToken cancellationToken) =>
        CreateBatchAsync(shopId, itemId, row.ToInboundInput(), supplier, actorUserId, cancellationToken);

    private static decimal ComputeUnitCost(decimal totalPurchaseCost, decimal quantity) =>
        decimal.Round(totalPurchaseCost / quantity, 2, MidpointRounding.AwayFromZero);
}
