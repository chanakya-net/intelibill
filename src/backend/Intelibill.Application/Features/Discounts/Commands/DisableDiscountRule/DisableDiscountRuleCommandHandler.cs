using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Discounts.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Discounts.Commands.DisableDiscountRule;

public sealed class DisableDiscountRuleCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IDiscountRuleRepository discountRuleRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<DiscountRuleDto>> Handle(
        DisableDiscountRuleCommand command,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", "User not found.");

        var shop = await shopRepository.GetByIdAsync(command.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(command.UserId, command.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var rule = await discountRuleRepository.GetByIdAsync(command.RuleId, cancellationToken);
        if (rule is null || rule.ShopId != command.ShopId)
            return Errors.Discount.DiscountRuleNotFound;

        var result = rule.Disable(command.Reason, DateTimeOffset.UtcNow, command.UserId);
        if (result.IsError)
            return result.Errors;

        discountRuleRepository.Update(rule);
        await unitOfWork.SaveChangesAsync(cancellationToken);

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

