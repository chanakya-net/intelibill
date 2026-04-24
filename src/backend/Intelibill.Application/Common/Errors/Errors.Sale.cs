using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Sale
    {
        public static Error ItemsRequired =>
            Error.Validation("Sale.ItemsRequired", "At least one sale item is required.");

        public static Error ItemNotFound(string barcode) =>
            Error.NotFound("Sale.ItemNotFound", $"Item with barcode '{barcode}' was not found.");

        public static Error BatchNotFound(string barcode, string batchNumber) =>
            Error.NotFound("Sale.BatchNotFound", $"Batch '{batchNumber}' for item with barcode '{barcode}' was not found.");

        public static Error BatchVoided(string barcode, string batchNumber) =>
            Error.Validation("Sale.BatchVoided", $"Batch '{batchNumber}' for item with barcode '{barcode}' is voided.");

        public static Error InsufficientStock(string barcode, string batchNumber) =>
            Error.Validation("Sale.InsufficientStock", $"Insufficient stock in batch '{batchNumber}' for item with barcode '{barcode}'.");

        public static Error InventoryAggregateNotFound(string barcode) =>
            Error.NotFound("Sale.InventoryAggregateNotFound", $"Inventory aggregate not found for item with barcode '{barcode}'.");

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

        public static Error CreditCustomerNotFound =>
            Error.NotFound("Sale.CreditCustomerNotFound", "Registered customer was not found for this credit or due sale.");

        public static Error CustomerIdentityMismatch =>
            Error.Validation("Sale.CustomerIdentityMismatch", "Customer id and customer phone refer to different customers.");
    }
}
