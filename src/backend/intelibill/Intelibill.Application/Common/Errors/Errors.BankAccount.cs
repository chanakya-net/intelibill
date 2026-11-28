using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class BankAccount
    {
        public static Error IfscCodeInvalid =>
            Error.Validation("BankAccount.IfscCodeInvalid", "IFSC code must be a valid Indian bank IFSC code.");

        public static Error BankAccountTypeInvalid =>
            Error.Validation("BankAccount.BankAccountTypeInvalid", "Bank account type must be Savings or Current.");

        public static Error NotFound =>
            Error.NotFound("BankAccount.NotFound", "Bank account not found.");

        public static Error Required =>
            Error.Validation("BankAccount.Required", "Bank name and account number are required.");
    }
}
