using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Customer
    {
        public static Error NameRequired =>
            Error.Validation("Customer.NameRequired", "Customer name is required.");

        public static Error PhoneNumberRequired =>
            Error.Validation("Customer.PhoneNumberRequired", "Phone number is required.");

        public static Error PhoneNumberInvalid =>
            Error.Validation("Customer.PhoneNumberInvalid", "Phone number must contain 7 to 15 digits and may include leading +.");

        public static Error CustomerNotFound =>
            Error.NotFound("Customer.CustomerNotFound", "Customer was not found.");

        public static Error CreditLimitInvalid =>
            Error.Validation("Customer.CreditLimitInvalid", "Credit limit must be between 0 and 99999999.99.");

        public static Error PaymentAmountMustBePositive =>
            Error.Validation("Customer.PaymentAmountMustBePositive", "Payment amount must be greater than zero.");

        public static Error UserIsNotOwnerOrManager =>
            Error.Forbidden("Customer.UserIsNotOwnerOrManager", "Only owner or manager can view customer account details.");

        public static Error GstinInvalid =>
            Error.Validation("Customer.GstinInvalid", "The customer's GSTIN is not valid: check its characters and check digit.");

        public static Error StateCodeInvalid =>
            Error.Validation("Customer.StateCodeInvalid", "Choose an Indian state or union territory for the customer.");

        public static Error GstinStateMismatch =>
            Error.Validation("Customer.GstinStateMismatch", "The customer's GSTIN belongs to a different state than the one chosen.");

        public static Error StatementWindowInvalid =>
            Error.Validation("Customer.StatementWindowInvalid", "The statement start date must be on or before its end date.");

        public static Error ShopOwnerNotFound =>
            Error.Unexpected("Customer.ShopOwnerNotFound", "Unable to resolve the shop owner for customer operations.");
    }
}
