using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Discounts.Commands.CreateDiscountRule;

public sealed record CreateDiscountRuleCommand(
    Guid UserId,
    Guid ShopId,
    DiscountRuleType RuleType,
    string Name,
    string? Description,
    Guid? InventoryBatchId,
    decimal Percentage,
    decimal? ThresholdAmount,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    bool BelowCostConfirmed,
    string? BelowCostConfirmationReason);

