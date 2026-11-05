using ErrorOr;

namespace Intelibill.Domain.Common;

public static partial class Errors
{
    public static class Sale
    {
        public static Error ReturnNoteRequired(string reason) =>
            Error.Validation("SaleReturn.NoteRequired", $"Return line note is required for {reason}.");

        public static Error ReturnPayoutDestinationRequired =>
            Error.Validation("SaleReturn.PayoutDestinationRequired", "Payout destination is required when payout amount is greater than zero.");

        public static Error ReturnPayoutDestinationInvalid =>
            Error.Validation("SaleReturn.PayoutDestinationInvalid", "Return payout destination must be a recognised value.");

        public static Error ReturnAccountingAlreadyPosted =>
            Error.Validation("SaleReturn.AccountingAlreadyPosted", "The return's accounting snapshot is already posted and cannot change.");

        public static Error ReturnAccountingVoided =>
            Error.Validation("SaleReturn.AccountingVoided", "A voided return cannot be posted.");

        public static Error ReturnAccountingPolicyRequired =>
            Error.Validation("SaleReturn.AccountingPolicyRequired", "The accounting policy version is required.");

        public static Error ReturnAccountingAmountInvalid =>
            Error.Validation("SaleReturn.AccountingAmountInvalid", "Accounting amounts must be non-negative with at most two decimal places.");

        public static Error ReturnAccountingLineMismatch =>
            Error.Validation("SaleReturn.AccountingLineMismatch", "Each return line must be classified once and its adjustment must equal its approved refund.");

        public static Error ReturnAccountingSettlementMismatch =>
            Error.Validation("SaleReturn.AccountingSettlementMismatch", "Settlement legs must equal the approved refund, split as recorded between due reduction and payout.");

        public static Error ReturnAccountingSettlementInvalid =>
            Error.Validation("SaleReturn.AccountingSettlementInvalid", "Each settlement leg must be positive and reference what settled it.");

        public static Error ItemsRequired =>
            Error.Validation("Sale.ItemsRequired", "At least one sale item is required.");

        public static Error PaidAmountInvalid =>
            Error.Validation("Sale.PaidAmountInvalid", "Paid amount cannot be negative.");

        public static Error DueAmountInvalid =>
            Error.Validation("Sale.DueAmountInvalid", "Due amount cannot be negative.");

        public static Error CreditNoteAppliedAmountInvalid =>
            Error.Validation("Sale.CreditNoteAppliedAmountInvalid", "Credit note applied amount cannot be negative.");

        public static Error CreditRequiresDueAmount =>
            Error.Validation("Sale.CreditRequiresDueAmount", "Credit sale must include a due amount greater than zero.");

        public static Error CustomerIdentityRequiredForDue =>
            Error.Validation("Sale.CustomerIdentityRequiredForDue", "Customer id or customer phone is required when due amount is greater than zero.");

        public static Error PaidAndDueAmountMismatch =>
            Error.Validation("Sale.PaidAndDueAmountMismatch", "Paid amount and due amount must match sale total.");

        public static Error InvalidLineReferences =>
            Error.Validation("Sale.InvalidLineReferences", "Sale line references are invalid for the specified line type.");
    }

    public static class Quantity
    {
        public static Error NotPositive(string unitLabel) =>
            Error.Validation("Quantity.NotPositive", $"Quantity in {unitLabel} must be greater than zero.");

        public static Error PrecisionExceeded(decimal value, string unitLabel, int scale) =>
            Error.Validation(
                "Quantity.PrecisionExceeded",
                scale == 0
                    ? $"Quantity {Format(value)} is not allowed: {unitLabel} is sold in whole units only."
                    : $"Quantity {Format(value)} is not allowed: {unitLabel} allows at most {scale} decimal places.");

        private static string Format(decimal value) =>
            (value / 1.000000000000000000000000000000000m).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static class ItemUnit
    {
        public static Error ChangeWithHistory =>
            Error.Conflict("Item.UnitChangeWithHistory", "The unit cannot change to a different precision because the item already has stock history.");
    }
}
