using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Discounts.Queries.GetDiscountRules;

public sealed record GetDiscountRulesQuery(
    Guid UserId,
    Guid ShopId,
    string? Status,
    DiscountRuleType? RuleType,
    string? Search,
    string? Sort,
    int PageNumber = 1,
    int PageSize = 20);

