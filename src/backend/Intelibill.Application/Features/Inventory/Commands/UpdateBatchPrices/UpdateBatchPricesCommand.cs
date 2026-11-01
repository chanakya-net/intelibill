namespace Intelibill.Application.Features.Inventory.Commands.UpdateBatchPrices;

/// <summary>
/// Changes a batch's retail prices and lot details against the batch version the editor was shown.
/// A null <paramref name="BatchNumber"/> keeps the current number.
/// </summary>
public sealed record UpdateBatchPricesCommand(
    Guid BatchId,
    Guid ActorUserId,
    Guid ActiveShopId,
    uint ExpectedVersion,
    decimal Mrp,
    decimal SalesPrice,
    decimal TaxRatePercent,
    bool TaxIncluded,
    string? BatchNumber,
    DateOnly? ExpiryDate,
    DateOnly? ManufacturingDate,
    string? Reason);
