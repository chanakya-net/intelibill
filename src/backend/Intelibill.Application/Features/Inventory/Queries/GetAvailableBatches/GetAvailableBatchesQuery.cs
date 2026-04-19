namespace Intelibill.Application.Features.Inventory.Queries.GetAvailableBatches;

public sealed record GetAvailableBatchesQuery(Guid UserId, Guid ShopId, string SearchTerm);
