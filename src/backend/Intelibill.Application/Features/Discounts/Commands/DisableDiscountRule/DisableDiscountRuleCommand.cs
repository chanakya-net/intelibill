namespace Intelibill.Application.Features.Discounts.Commands.DisableDiscountRule;

public sealed record DisableDiscountRuleCommand(
    Guid UserId,
    Guid ShopId,
    Guid RuleId,
    string? Reason);

