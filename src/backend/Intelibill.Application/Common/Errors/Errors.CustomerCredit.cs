using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    /// <summary>Refusals of new customer due under the customer's credit policy.</summary>
    public static class CustomerCredit
    {
        public static Error CustomerInactive =>
            Error.Conflict("Customer.CreditCustomerInactive", "This customer is inactive and cannot take new due. Collect the full amount or reactivate the customer.");

        public static Error NotExtended =>
            Error.Conflict("Customer.CreditNotExtended", "This customer has no credit. Collect the full amount, or ask an owner to change the customer's credit policy.");

        public static Error LimitExceeded =>
            Error.Conflict("Customer.CreditLimitExceeded", "This due would take the customer over their credit limit. Collect more now, or ask an owner or manager to approve an override.");

        public static Error OverrideNotPermitted =>
            Error.Forbidden("Customer.CreditOverrideNotPermitted", "Only an owner or manager can approve going over a customer's credit limit.");

        public static Error OverrideStale =>
            Error.Conflict("Customer.CreditOverrideStale", "The customer's balance changed after the override was approved. Review the account and approve again.");

        public static Error OverrideReasonRequired =>
            Error.Validation("Customer.CreditOverrideReasonRequired", "Give a reason (up to 500 characters) for going over the credit limit.");

        public static Error Busy =>
            Error.Conflict("Customer.CreditBusy", "Another sale, payment or return for this customer is in progress. Try again.");

        public static Error SettingsChangeNotPermitted =>
            Error.Forbidden("Customer.CreditSettingsChangeNotPermitted", "Only an owner or manager can set a customer's credit limit, policy or terms.");
    }
}
