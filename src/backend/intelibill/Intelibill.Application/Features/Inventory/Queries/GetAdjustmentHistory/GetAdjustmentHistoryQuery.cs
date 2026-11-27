using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Inventory.Queries.GetAdjustmentHistory;

public sealed record GetAdjustmentHistoryQuery(
    Guid UserId,
    Guid ShopId,
    int PageNumber = 1,
    int PageSize = 25,
    Guid? ItemId = null,
    Guid? BatchId = null,
    InventoryAdjustmentDirection? Direction = null,
    InventoryAdjustmentReason? Reason = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    bool IncludeVoided = false);
