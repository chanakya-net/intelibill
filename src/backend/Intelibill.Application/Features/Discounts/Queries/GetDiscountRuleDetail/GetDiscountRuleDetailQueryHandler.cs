using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Discounts.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Discounts.Queries.GetDiscountRuleDetail;

public sealed class GetDiscountRuleDetailQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IDiscountRuleRepository discountRuleRepository)
{
    public async Task<ErrorOr<DiscountRuleDto>> Handle(
        GetDiscountRuleDetailQuery query,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(query.UserId, cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", "User not found.");

        var shop = await shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(query.UserId, query.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var rule = await discountRuleRepository.GetByIdAsync(query.RuleId, cancellationToken);
        if (rule is null || rule.ShopId != query.ShopId)
            return Errors.Discount.DiscountRuleNotFound;

        return ToDto(rule);
    }

    private static DiscountRuleDto ToDto(DiscountRule rule) =>
        new(
            rule.Id,
            rule.RuleType,
            rule.Name,
            rule.Description,
            rule.InventoryBatchId,
            rule.Percentage,
            rule.ThresholdAmount,
            rule.StartsAt,
            rule.EndsAt,
            rule.IsActive,
            rule.DisabledAt,
            rule.DisabledReason,
            rule.BelowCostConfirmed,
            rule.BelowCostConfirmationReason,
            rule.ReplacesRuleId,
            rule.ReplacedByRuleId,
            rule.CreatedAt,
            rule.UpdatedAt);
}

