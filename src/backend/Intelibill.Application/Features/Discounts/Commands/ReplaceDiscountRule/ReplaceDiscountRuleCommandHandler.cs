using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Discounts.DTOs;
using Intelibill.Application.Features.Discounts.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Discounts.Commands.ReplaceDiscountRule;

public sealed class ReplaceDiscountRuleCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IDiscountRuleRepository discountRuleRepository,
    DiscountRuleValidationService validationService,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<DiscountRuleDto>> HandleAsync(
        ReplaceDiscountRuleCommand command,
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

        var existing = await discountRuleRepository.GetByIdAsync(command.RuleId, cancellationToken);
        if (existing is null || existing.ShopId != command.ShopId)
            return Errors.Discount.DiscountRuleNotFound;

        if (!existing.IsActive)
            return Errors.Discount.DiscountRuleAlreadyDisabled;

        var preview = await validationService.PreviewAsync(
            command.ShopId,
            command.RuleType,
            command.Percentage,
            command.ThresholdAmount,
            command.InventoryBatchId,
            command.StartsAt,
            command.EndsAt,
            command.BelowCostConfirmed,
            excludeRuleId: command.RuleId,
            cancellationToken);

        if (preview.Errors.Count > 0)
            return Errors.Discount.InvalidDiscountRule(preview.Errors);

        if (preview.BelowCostSample.Count > 0 && command.BelowCostConfirmed && string.IsNullOrWhiteSpace(command.BelowCostConfirmationReason))
            return Errors.Discount.BelowCostConfirmationReasonRequired;

        var newRuleResult = DiscountRule.Create(
            command.ShopId,
            command.RuleType,
            command.Name,
            command.Description,
            command.InventoryBatchId,
            command.Percentage,
            command.ThresholdAmount,
            command.StartsAt,
            command.EndsAt,
            command.BelowCostConfirmed,
            command.BelowCostConfirmationReason,
            command.UserId);

        if (newRuleResult.IsError)
            return newRuleResult.Errors;

        var now = DateTimeOffset.UtcNow;

        existing.ReplaceWith(newRuleResult.Value.Id, now, command.UserId, command.DisabledReason);
        newRuleResult.Value.MarkAsReplacement(existing.Id);

        discountRuleRepository.Update(existing);
        await discountRuleRepository.AddAsync(newRuleResult.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(newRuleResult.Value);
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
