using ErrorOr;

namespace Intelibill.Domain.Common;

public static partial class Errors
{
    public static class CustomerCredit
    {
        public static Error PolicyModeInvalid =>
            Error.Validation("Customer.CreditPolicyModeInvalid", "Choose a credit policy: no credit or an enforced limit.");

        public static Error EnforcedPolicyRequiresLimit =>
            Error.Validation("Customer.EnforcedCreditRequiresLimit", "An enforced credit policy needs a credit limit above zero. Choose no credit instead.");

        public static Error TermsDaysInvalid =>
            Error.Validation("Customer.CreditTermsDaysInvalid", "Credit terms must be between 0 and 365 days.");
    }
}
