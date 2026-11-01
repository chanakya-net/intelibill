namespace Intelibill.Application.Features.Inventory.Commands.CorrectStockCount;

/// <summary>
/// Records a physical count of one batch taken against the batch version the counter was shown.
/// </summary>
public sealed record CorrectStockCountCommand(
    Guid BatchId,
    Guid ActorUserId,
    Guid ActiveShopId,
    uint ExpectedVersion,
    decimal CountedQuantity,
    string Reason,
    string? OperationKey);
