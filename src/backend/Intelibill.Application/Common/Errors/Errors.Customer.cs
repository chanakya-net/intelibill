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
    }
}
