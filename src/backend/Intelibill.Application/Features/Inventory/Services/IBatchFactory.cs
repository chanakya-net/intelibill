using ErrorOr;
using Intelibill.Application.Features.Inventory.Commands.AddInventoryBatch;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Inventory.Services;

/// <param name="LedgerEntry">The goods-received payable; null exactly when the receipt value rounds to zero.</param>
public sealed record BatchCreationResult(
    InventoryBatch Batch,
    StockTransaction StockTransaction,
    SupplierLedgerEntry? LedgerEntry);

public interface IBatchFactory
{
    Task<ErrorOr<BatchCreationResult>> CreateBatchAsync(
        Guid shopId,
        Guid itemId,
        InboundInventoryLineInput row,
        Supplier supplier,
        Guid actorUserId,
        CancellationToken cancellationToken);

    Task<ErrorOr<BatchCreationResult>> CreateBatchAsync(
        Guid shopId,
        Guid itemId,
        AddInventoryBatchRowCommand row,
        Supplier supplier,
        Guid actorUserId,
        CancellationToken cancellationToken);
}
