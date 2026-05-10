using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Discounts.DTOs;

public sealed record DiscountRuleDto(
    Guid Id,
    DiscountRuleType RuleType,
    string Name,
    string? Description,
    Guid? InventoryBatchId,
    decimal Percentage,
    decimal? ThresholdAmount,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    bool IsActive,
    DateTimeOffset? DisabledAt,
    string? DisabledReason,
    bool BelowCostConfirmed,
    string? BelowCostConfirmationReason,
    Guid? ReplacesRuleId,
    Guid? ReplacedByRuleId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

