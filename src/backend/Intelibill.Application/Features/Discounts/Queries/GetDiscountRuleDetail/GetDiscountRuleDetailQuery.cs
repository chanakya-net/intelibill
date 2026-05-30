namespace Intelibill.Application.Features.Discounts.Queries.GetDiscountRuleDetail;

public sealed record GetDiscountRuleDetailQuery(
    Guid UserId,
    Guid ShopId,
    Guid RuleId);

