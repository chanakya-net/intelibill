namespace Intelibill.Application.Features.Discounts.DTOs;

public sealed record DiscountRulePreviewDto(
    int AffectedCount,
    IReadOnlyList<DiscountRulePreviewBatchDto> AffectedSample,
    IReadOnlyList<DiscountRulePreviewBatchDto> BelowCostSample,
    decimal? SafeMaxPercentage,
    IReadOnlyList<DiscountRulePreviewMessageDto> Errors,
    IReadOnlyList<DiscountRulePreviewMessageDto> Infos);

public sealed record DiscountRulePreviewBatchDto(
    Guid BatchId,
    string ItemName,
    string BatchNumber,
    decimal SalesPrice,
    decimal CostPrice,
    decimal DiscountedPrice);

public sealed record DiscountRulePreviewMessageDto(string Code, string Message);
