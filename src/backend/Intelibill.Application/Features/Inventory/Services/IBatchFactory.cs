using ErrorOr;
using Intelibill.Application.Features.Inventory.Commands.AddInventoryBatch;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Inventory.Services;

public sealed record BatchCreationResult(
    InventoryBatch Batch,
    StockTransaction StockTransaction,
    SupplierLedgerEntry LedgerEntry);

public interface IBatchFactory
{
    Task<ErrorOr<BatchCreationResult>> CreateBatchAsync(
        Guid shopId,
        Guid itemId,
        AddInventoryBatchRowCommand row,
        Supplier supplier,
        Guid actorUserId,
        CancellationToken cancellationToken);
}
