using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class DiscountRule : BaseEntity
{
    public Guid ShopId { get; private set; }
    public DiscountRuleType RuleType { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid? InventoryBatchId { get; private set; }
    public decimal Percentage { get; private set; }
    public decimal? ThresholdAmount { get; private set; }
    public DateTimeOffset? StartsAt { get; private set; }
    public DateTimeOffset? EndsAt { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? DisabledAt { get; private set; }
    public string? DisabledReason { get; private set; }
    public bool BelowCostConfirmed { get; private set; }
    public string? BelowCostConfirmationReason { get; private set; }
    public Guid? ReplacesRuleId { get; private set; }
    public Guid? ReplacedByRuleId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    private DiscountRule() { }

    public static ErrorOr<DiscountRule> Create(
        Guid shopId,
        DiscountRuleType ruleType,
        string name,
        string? description,
        Guid? inventoryBatchId,
        decimal percentage,
        decimal? thresholdAmount,
        DateTimeOffset? startsAt,
        DateTimeOffset? endsAt,
        bool belowCostConfirmed,
        string? belowCostConfirmationReason,
        Guid createdBy)
    {
        var validation = Validate(ruleType, name, percentage, thresholdAmount, startsAt, endsAt);
        if (validation.IsError)
        {
            return validation.Errors;
        }

        return new DiscountRule
        {
            ShopId = shopId,
            RuleType = ruleType,
            Name = name.Trim(),
            Description = Normalize(description),
            InventoryBatchId = inventoryBatchId,
            Percentage = percentage,
            ThresholdAmount = thresholdAmount,
            StartsAt = startsAt,
            EndsAt = endsAt,
            IsActive = true,
            BelowCostConfirmed = belowCostConfirmed,
            BelowCostConfirmationReason = Normalize(belowCostConfirmationReason),
            CreatedBy = createdBy,
        };
    }

    public ErrorOr<Success> Disable(string? reason, DateTimeOffset disabledAt, Guid updatedBy)
    {
        if (!IsActive)
        {
            return Error.Validation("DiscountRule.AlreadyDisabled", "Discount rule is already disabled.");
        }

        IsActive = false;
        DisabledAt = disabledAt;
        DisabledReason = Normalize(reason);
        UpdatedBy = updatedBy;

        return Result.Success;
    }

    public void ReplaceWith(Guid newRuleId, DateTimeOffset disabledAt, Guid updatedBy)
    {
        ReplacedByRuleId = newRuleId;
        IsActive = false;
        DisabledAt = disabledAt;
        DisabledReason = "Replaced by new version.";
        UpdatedBy = updatedBy;
    }

    public void MarkAsReplacement(Guid oldRuleId)
    {
        ReplacesRuleId = oldRuleId;
    }

    private static ErrorOr<Success> Validate(
        DiscountRuleType ruleType,
        string name,
        decimal percentage,
        decimal? thresholdAmount,
        DateTimeOffset? startsAt,
        DateTimeOffset? endsAt)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("DiscountRule.NameRequired", "Discount rule name is required.");
        }

        if (percentage <= 0 || percentage > 100)
        {
            return Error.Validation("DiscountRule.PercentageOutOfRange", "Percentage must be greater than 0 and at most 100.");
        }

        if (ruleType == DiscountRuleType.SaleThresholdPercentage && thresholdAmount is null)
        {
            return Error.Validation("DiscountRule.ThresholdAmountRequired", "Threshold amount is required for sale threshold percentage rules.");
        }

        if (thresholdAmount is not null && thresholdAmount <= 0)
        {
            return Error.Validation("DiscountRule.ThresholdAmountMustBePositive", "Threshold amount must be greater than zero.");
        }

        if (startsAt.HasValue && endsAt.HasValue && endsAt <= startsAt)
        {
            return Error.Validation("DiscountRule.EndsAtMustBeAfterStartsAt", "EndsAt must be after StartsAt.");
        }

        return Result.Success;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
