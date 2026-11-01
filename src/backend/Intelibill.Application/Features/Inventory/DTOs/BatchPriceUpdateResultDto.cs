namespace Intelibill.Application.Features.Inventory.DTOs;

/// <param name="Version">The batch version after the edit; the next edit must be made against it.</param>
public sealed record BatchPriceUpdateResultDto(Guid BatchId, uint Version, Guid? PriceRevisionId);
