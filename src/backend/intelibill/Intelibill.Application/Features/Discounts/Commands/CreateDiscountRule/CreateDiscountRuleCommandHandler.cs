using Intelibill.Domain.Enums;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Discounts.DTOs;
using Intelibill.Application.Features.Discounts.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Discounts.Commands.CreateDiscountRule;

public sealed class CreateDiscountRuleCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IDiscountRuleRepository discountRuleRepository,
    DiscountRuleValidationService validationService,
    IUnitOfWork unitOfWork,
    IShopLicenceFence licenceFence,
    ILiveSessionAuthority authority)
{
    [LicenceAction(LicenceAction.OperationalCapacity)]
    public async Task<ErrorOr<DiscountRuleDto>> HandleAsync(
        CreateDiscountRuleCommand command,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
            return Errors.User.NotFound;

        var shop = await shopRepository.GetByIdAsync(command.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(command.UserId, command.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;
        if (membership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Shop.PermissionDenied;

        var result = await PostingTransaction.RunAndSaveAsync<DiscountRule>(unitOfWork,
            ct => WriteAsync(command, ct), cancellationToken);
        return result.IsError ? result.Errors : ToDto(result.Value);
    }

    private async Task<ErrorOr<DiscountRule>> WriteAsync(CreateDiscountRuleCommand command, CancellationToken cancellationToken)
    {
        var lease = await licenceFence.EnterForSessionAsync(authority, command.Session, command.UserId,
            command.ShopId, LicenceAction.OperationalCapacity, cancellationToken);
        if (lease.IsError) return lease.Errors;
        if (lease.Value.CurrentDecision().EnforcedDenial() is { } denial) return denial;

        var preview = await validationService.PreviewAsync(
            command.ShopId,
            command.RuleType,
            command.Percentage,
            command.ThresholdAmount,
            command.InventoryBatchId,
            command.StartsAt,
            command.EndsAt,
            command.BelowCostConfirmed,
            excludeRuleId: null,
            cancellationToken);

        if (preview.Errors.Count > 0)
            return Errors.Discount.InvalidDiscountRule(preview.Errors);

        if (preview.BelowCostSample.Count > 0 && command.BelowCostConfirmed && string.IsNullOrWhiteSpace(command.BelowCostConfirmationReason))
            return Errors.Discount.BelowCostConfirmationReasonRequired;

        if (lease.Value.CurrentDecision().EnforcedDenial() is { } currentDenial) return currentDenial;

        var ruleResult = DiscountRule.Create(
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

        if (ruleResult.IsError)
            return ruleResult.Errors;

        await discountRuleRepository.AddAsync(ruleResult.Value, cancellationToken);

        return ruleResult.Value;
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
