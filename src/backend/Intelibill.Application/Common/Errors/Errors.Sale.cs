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

        public static Error NotFound(Guid saleId) =>
            Error.NotFound("Sale.NotFound", $"Sale '{saleId}' was not found.");

        public static Error ReturnItemsRequired =>
            Error.Validation("SaleReturn.ItemsRequired", "At least one return item is required.");

        public static Error ReturnQuantityMustBePositive(Guid saleItemId) =>
            Error.Validation("SaleReturn.QuantityMustBePositive", $"Return quantity for sale item '{saleItemId}' must be greater than zero.");

        public static Error ReturnDuplicateSaleItem(Guid saleItemId) =>
            Error.Validation("SaleReturn.DuplicateSaleItem", $"Sale item '{saleItemId}' appears more than once in the return request.");

        public static Error ReturnSaleItemNotFound(Guid saleItemId) =>
            Error.Validation("SaleReturn.SaleItemNotFound", $"Sale item '{saleItemId}' does not belong to this sale.");

        public static Error ReturnQuantityExceedsRemaining(Guid saleItemId, decimal remainingQuantity) =>
            Error.Validation("SaleReturn.QuantityExceedsRemaining", $"Return quantity for sale item '{saleItemId}' exceeds remaining returnable quantity '{remainingQuantity}'.");

        public static Error ReturnBatchNotFound(Guid batchId) =>
            Error.Validation("SaleReturn.BatchNotFound", $"Original batch '{batchId}' was not found for this sale item.");

        public static Error ReturnBatchVoided(string batchNumber) =>
            Error.Validation("SaleReturn.BatchVoided", $"Restockable return is blocked because original batch '{batchNumber}' is voided.");

        public static Error ReturnBatchExpired(string batchNumber) =>
            Error.Validation("SaleReturn.BatchExpired", $"Restockable return is blocked because original batch '{batchNumber}' is expired.");

        public static Error ReturnForbidden =>
            Error.Forbidden("SaleReturn.Forbidden", "Only owners and managers can record sale returns.");

        public static Error ReturnPayoutMethodRequired =>
            Error.Validation("SaleReturn.PayoutMethodRequired", "Payout method is required when payout amount is greater than zero.");

        public static Error ReturnPayoutMethodInvalid =>
            Error.Validation("SaleReturn.PayoutMethodInvalid", "Return payout method must be Cash, UPI, or Card.");

        public static Error ReturnCustomerDueNotSupported =>
            Error.Validation("SaleReturn.CustomerDueNotSupported", "Customer due returns are not supported yet.");

        public static Error ReturnNoteRequired(string reason) =>
            Error.Validation("SaleReturn.NoteRequired", $"Return line note is required for {reason}.");

        public static Error ReturnInventoryAggregateNotFound(Guid itemId) =>
            Error.NotFound("SaleReturn.InventoryAggregateNotFound", $"Inventory aggregate was not found for item '{itemId}'.");
    }
}
