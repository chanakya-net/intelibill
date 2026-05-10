using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Discounts.DTOs;
using Intelibill.Application.Features.Discounts.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Discounts.Queries.PreviewDiscountRule;

public sealed class PreviewDiscountRuleQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    DiscountRuleValidationService validationService)
{
    public async Task<ErrorOr<DiscountRulePreviewDto>> Handle(
        PreviewDiscountRuleQuery query,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(query.ActorUserId, cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", "User not found.");

        var shop = await shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(query.ActorUserId, query.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var preview = await validationService.PreviewAsync(
            query.ShopId,
            query.RuleType,
            query.Percentage,
            query.ThresholdAmount,
            query.InventoryBatchId,
            query.StartsAt,
            query.EndsAt,
            query.BelowCostConfirmed,
            excludeRuleId: null,
            cancellationToken);

        if (preview.Errors.Count > 0 && preview.Errors.Any(e => e.Code == "discount.error.unsupported_rule_type"))
            return Errors.Discount.UnsupportedRuleType(query.RuleType);

        return preview;
    }
}
