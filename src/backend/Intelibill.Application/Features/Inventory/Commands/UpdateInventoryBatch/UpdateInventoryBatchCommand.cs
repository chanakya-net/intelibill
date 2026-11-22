using Intelibill.Application.Common.Security;
using ErrorOr;

namespace Intelibill.Application.Features.Inventory.Commands.UpdateInventoryBatch;

public sealed record UpdateInventoryBatchCommand(
    Guid BatchId,
    Guid UserId,
    Guid ShopId,
    string? NewBatchNumber,
    decimal Quantity,
    decimal CostPrice,
    decimal Mrp,
    decimal SalesPrice,
    decimal TaxRatePercent,
    bool TaxIncluded,
    DateOnly? ExpiryDate,
    DateOnly? ManufacturingDate,
    Guid? SupplierId,
    string? Notes,
    DateOnly? EntryDate,
    SessionClaims? Session,
    uint? ExpectedVersion = null);
