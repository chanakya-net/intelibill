using ErrorOr;
using Intelibill.Application.Features.Discounts.DTOs;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Discount
    {
        public static readonly Error NameRequired =
            Error.Validation("DiscountRule.NameRequired", "Discount rule name is required.");

        public static readonly Error PercentageOutOfRange =
            Error.Validation("DiscountRule.PercentageOutOfRange", "Percentage must be greater than 0 and at most 100.");

        public static readonly Error BelowCostConfirmationReasonRequired =
            Error.Validation("DiscountRule.BelowCostConfirmationReasonRequired", "Below-cost confirmation reason is required when belowCostConfirmed=true.");

        public static readonly Error DiscountRuleNotFound =
            Error.NotFound("DiscountRule.NotFound", "Discount rule not found.");

        public static readonly Error DiscountRuleAlreadyDisabled =
            Error.Validation("DiscountRule.AlreadyDisabled", "Discount rule is already disabled.");

        public static Error InvalidDiscountRule(IReadOnlyList<DiscountRulePreviewMessageDto> previewErrors)
        {
            var message = previewErrors.Count == 0
                ? "Discount rule is invalid."
                : $"Discount rule is invalid: {previewErrors[0].Message}";
            return Error.Validation("DiscountRule.Invalid", message);
        }

        public static Error UnsupportedRuleType(DiscountRuleType ruleType) =>
            Error.Validation("Discount.UnsupportedRuleType", $"Discount rule type '{ruleType}' is not supported.");
    }
}
