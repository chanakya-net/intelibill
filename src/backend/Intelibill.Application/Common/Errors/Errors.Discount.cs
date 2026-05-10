using ErrorOr;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Discount
    {
        public static Error UnsupportedRuleType(DiscountRuleType ruleType) =>
            Error.Validation("Discount.UnsupportedRuleType", $"Discount rule type '{ruleType}' is not supported.");
    }
}
