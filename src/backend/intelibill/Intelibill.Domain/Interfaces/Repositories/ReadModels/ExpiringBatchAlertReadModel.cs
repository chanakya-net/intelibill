namespace Intelibill.Domain.Interfaces.Repositories;

public sealed record ExpiringBatchAlertReadModel(
    Guid InventoryBatchId,
    string ItemName,
    string BatchNumber,
    DateOnly ExpiryDate,
    decimal Quantity);
