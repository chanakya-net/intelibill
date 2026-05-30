using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Discounts.Commands.ReplaceDiscountRule;

public sealed record ReplaceDiscountRuleCommand(
    Guid UserId,
    Guid ShopId,
    Guid RuleId,
    DiscountRuleType RuleType,
    string Name,
    string? Description,
    Guid? InventoryBatchId,
    decimal Percentage,
    decimal? ThresholdAmount,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    bool BelowCostConfirmed,
    string? BelowCostConfirmationReason,
    string? DisabledReason);

