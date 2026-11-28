using Intelibill.Domain.Enums;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Common.Posting;
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
    IUnitOfWork unitOfWork,
    IShopLicenceFence licenceFence,
    ILiveSessionAuthority authority)
{
    [LicenceAction(LicenceAction.SourceCorrection)]
    public async Task<ErrorOr<DiscountRuleDto>> Handle(
        DisableDiscountRuleCommand command,
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

    private async Task<ErrorOr<DiscountRule>> WriteAsync(DisableDiscountRuleCommand command, CancellationToken cancellationToken)
    {
        var lease = await licenceFence.EnterForSessionAsync(authority, command.Session, command.UserId,
            command.ShopId, LicenceAction.SourceCorrection, cancellationToken);
        if (lease.IsError) return lease.Errors;
        if (lease.Value.CurrentDecision().EnforcedDenial() is { } denial) return denial;

        var rule = await discountRuleRepository.GetByIdAsync(command.RuleId, cancellationToken);
        if (rule is null || rule.ShopId != command.ShopId)
            return Errors.Discount.DiscountRuleNotFound;

        if (lease.Value.CurrentDecision().ExistingSourceDenial(rule.CreatedAt) is { } sourceDenial)
            return sourceDenial;

        var result = rule.Disable(command.Reason, DateTimeOffset.UtcNow, command.UserId);
        if (result.IsError)
            return result.Errors;

        discountRuleRepository.Update(rule);

        return rule;
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

