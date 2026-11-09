namespace Intelibill.Domain.ValueObjects;

public sealed record SaleExpiryDecision(Guid SaleId, Guid ItemId, Guid InventoryBatchId, decimal Quantity,
    DateOnly? ExpiryDate, DateOnly? ManufacturingDate, ExpiryPolicy Policy, long CatalogVersion,
    uint BatchVersion, DateOnly BusinessDate, string? ReasonCode, bool Warning, Guid? ApprovedBy,
    string? ApprovalReason, DateTimeOffset? ApprovedAt);
