namespace Intelibill.Application.Features.Inventory.Commands.VoidBatch;

public sealed record VoidBatchCommand(
    Guid BatchId,
    Guid ActorUserId,
    Guid ActiveShopId);