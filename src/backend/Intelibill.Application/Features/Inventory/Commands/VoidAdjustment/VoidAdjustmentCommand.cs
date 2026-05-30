namespace Intelibill.Application.Features.Inventory.Commands.VoidAdjustment;

public sealed record VoidAdjustmentCommand(
    Guid AdjustmentId,
    Guid ActorUserId,
    Guid ActiveShopId,
    string Reason);
