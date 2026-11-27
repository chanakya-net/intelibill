namespace Intelibill.Application.Features.Inventory.DTOs;

public sealed record VoidBatchResultDto(
    Guid BatchId,
    decimal OriginalQuantity,
    decimal RemainingQuantity,
    decimal? LedgerReversalAmount);
