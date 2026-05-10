using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Discounts.Queries.PreviewDiscountRule;

public sealed record PreviewDiscountRuleQuery(
    Guid ActorUserId,
    Guid ShopId,
    DiscountRuleType RuleType,
    decimal Percentage,
    decimal? ThresholdAmount,
    Guid? InventoryBatchId,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    bool BelowCostConfirmed);
