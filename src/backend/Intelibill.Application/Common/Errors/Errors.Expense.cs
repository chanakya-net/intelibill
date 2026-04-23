using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Expense
    {
        public static Error CategoryNameRequired =>
            Error.Validation("Expense.CategoryNameRequired", "Category name is required.");

        public static Error PaidToRequired =>
            Error.Validation("Expense.PaidToRequired", "Paid to is required.");

        public static Error AmountMustBePositive =>
            Error.Validation("Expense.AmountMustBePositive", "Amount must be greater than zero.");

        public static Error NotFound =>
            Error.NotFound("Expense.NotFound", "Expense was not found.");

        public static Error AlreadyVoided =>
            Error.Validation("Expense.AlreadyVoided", "Expense is already voided and cannot be corrected.");

        public static Error Forbidden =>
            Error.Forbidden("Expense.Forbidden", "Only owner or manager can record or correct expenses.");
    }
}
