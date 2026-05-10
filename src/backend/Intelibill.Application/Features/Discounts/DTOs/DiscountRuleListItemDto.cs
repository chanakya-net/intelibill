using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Discounts.DTOs;

public sealed record DiscountRuleListItemDto(
    Guid Id,
    DiscountRuleType RuleType,
    string Name,
    bool IsActive,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    DateTimeOffset CreatedAt);

