using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Inventory.Commands.CreateAdjustment;

public sealed record CreateAdjustmentCommand(
    Guid BatchId,
    Guid ActorUserId,
    Guid ActiveShopId,
    InventoryAdjustmentDirection Direction,
    InventoryAdjustmentReason Reason,
    decimal Quantity,
    DateTimeOffset? PerformedAt,
    string? Notes);
