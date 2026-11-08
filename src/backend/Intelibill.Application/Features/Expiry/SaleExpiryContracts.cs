using Intelibill.Domain.ValueObjects;
namespace Intelibill.Application.Features.Expiry;

public sealed record ExpiryApprovalRequest(Guid InventoryBatchId, decimal Quantity, uint BatchVersion, long PolicyVersion, long CatalogVersion, string Reason);
public sealed record SaleExpiryLineDecision(Guid ItemId, Guid InventoryBatchId, decimal Quantity, uint BatchVersion, SellabilityDecision Decision);
