using ErrorOr;

namespace Intelibill.Domain.Common;

public static partial class Errors
{
    public static class Sale
    {
        public static Error ReturnNoteRequired(string reason) =>
            Error.Validation("SaleReturn.NoteRequired", $"Return line note is required for {reason}.");

        public static Error ReturnPayoutMethodRequired =>
            Error.Validation("SaleReturn.PayoutMethodRequired", "Payout method is required when payout amount is greater than zero.");

        public static Error ReturnPayoutMethodInvalid =>
            Error.Validation("SaleReturn.PayoutMethodInvalid", "Return payout method must be Cash, UPI, or Card.");

        public static Error ItemsRequired =>
            Error.Validation("Sale.ItemsRequired", "At least one sale item is required.");

        public static Error PaidAmountInvalid =>
            Error.Validation("Sale.PaidAmountInvalid", "Paid amount cannot be negative.");

        public static Error DueAmountInvalid =>
            Error.Validation("Sale.DueAmountInvalid", "Due amount cannot be negative.");

        public static Error CreditRequiresDueAmount =>
            Error.Validation("Sale.CreditRequiresDueAmount", "Credit sale must include a due amount greater than zero.");

        public static Error CustomerIdentityRequiredForDue =>
            Error.Validation("Sale.CustomerIdentityRequiredForDue", "Customer id or customer phone is required when due amount is greater than zero.");

        public static Error PaidAndDueAmountMismatch =>
            Error.Validation("Sale.PaidAndDueAmountMismatch", "Paid amount and due amount must match sale total.");

        public static Error InvalidLineReferences =>
            Error.Validation("Sale.InvalidLineReferences", "Sale line references are invalid for the specified line type.");
    }
}
