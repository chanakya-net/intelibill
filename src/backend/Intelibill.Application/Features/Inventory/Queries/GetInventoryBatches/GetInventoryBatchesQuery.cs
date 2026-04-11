using ErrorOr;
using Intelibill.Application.Features.Inventory.DTOs;

namespace Intelibill.Application.Features.Inventory.Queries.GetInventoryBatches;

public sealed record GetInventoryBatchesQuery(Guid UserId, Guid ShopId);
