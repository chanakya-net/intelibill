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

        public static Error ItemInactive(string barcode) =>
            Error.Validation("Sale.ItemInactive", $"Item with barcode '{barcode}' is inactive.");

        public static Error BatchNotFound(string barcode, string batchNumber) =>
            Error.NotFound("Sale.BatchNotFound", $"Batch '{batchNumber}' for item with barcode '{barcode}' was not found.");

        public static Error BatchVoided(string barcode, string batchNumber) =>
            Error.Validation("Sale.BatchVoided", $"Batch '{batchNumber}' for item with barcode '{barcode}' is voided.");

        public static Error InsufficientStock(string barcode, string batchNumber) =>
            Error.Validation("Sale.InsufficientStock", $"Insufficient stock in batch '{batchNumber}' for item with barcode '{barcode}'.");

        public static Error InventoryAggregateNotFound(string barcode) =>
            Error.NotFound("Sale.InventoryAggregateNotFound", $"Inventory aggregate not found for item with barcode '{barcode}'.");

        public static Error InvalidTaxRatePercent =>
            Error.Validation("Sale.InvalidTaxRatePercent", "Tax rate must be between 0 and 100 with up to 2 decimal places.");

        public static Error InvalidHsnCode =>
            Error.Validation("Sale.InvalidHsnCode", "HSN code must be 4 to 8 digits.");

        public static Error ServiceNotFound =>
            Error.NotFound("Sale.ServiceNotFound", "Service was not found.");

        public static Error ServiceInactive =>
            Error.Validation("Sale.ServiceInactive", "Service is inactive.");

        public static Error ServiceDiscountNotSupported =>
            Error.Validation("Sale.ServiceDiscountNotSupported", "Service line discounts are not supported.");

        public static Error InvalidLineType =>
            Error.Validation("Sale.InvalidLineType", "Sale line type is invalid.");

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

        public static Error IdempotencyKeyRequired =>
            Error.Validation("Sale.IdempotencyKeyRequired", "Idempotency key is required.");

        public static Error IdempotencyKeyTooLong =>
            Error.Validation("Sale.IdempotencyKeyTooLong", "Idempotency key must be 128 characters or less.");

        public static Error IdempotencyConflict =>
            Error.Conflict("Sale.IdempotencyConflict", "Idempotency key has already been used with different request content.");

        public static Error OfflineSalesRequired =>
            Error.Validation("Sale.OfflineSalesRequired", "At least one offline sale is required.");

        public static Error OfflineBatchLimitExceeded =>
            Error.Validation("Sale.OfflineBatchLimitExceeded", "Offline sale sync supports up to 50 sales per request.");

        public static Error ClientSaleIdRequired =>
            Error.Validation("Sale.ClientSaleIdRequired", "Client sale id is required.");

        public static Error ClientSaleIdTooLong =>
            Error.Validation("Sale.ClientSaleIdTooLong", "Client sale id must be 120 characters or less.");

        public static Error InvoiceNumberRequired =>
            Error.Validation("Sale.InvoiceNumberRequired", "Invoice number is required.");

        public static Error InvoiceNumberTooLong =>
            Error.Validation("Sale.InvoiceNumberTooLong", "Invoice number must be 40 characters or less.");

        public static Error InvoiceNumberAlreadyUsed =>
            Error.Conflict("Sale.InvoiceNumberAlreadyUsed", "Invoice number is already used for another sale.");

        public static Error InvoiceLeaseNotFound =>
            Error.Validation("Sale.InvoiceLeaseNotFound", "Invoice number is not reserved for this device.");

        public static Error PaidAndDueAmountMismatch =>
            Error.Validation("Sale.PaidAndDueAmountMismatch", "Paid amount and due amount must match sale total.");

        public static Error TotalAmountInvalid =>
            Error.Validation("Sale.TotalAmountInvalid", "Total amount cannot be negative.");

        public static Error TotalTaxAmountInvalid =>
            Error.Validation("Sale.TotalTaxAmountInvalid", "Total tax amount cannot be negative.");

        public static Error SubtotalBeforeDiscountInvalid =>
            Error.Validation("Sale.SubtotalBeforeDiscountInvalid", "Subtotal before discount cannot be negative.");

        public static Error TotalBeforeDiscountInvalid =>
            Error.Validation("Sale.TotalBeforeDiscountInvalid", "Total before discount cannot be negative.");

        public static Error TotalDiscountAmountInvalid =>
            Error.Validation("Sale.TotalDiscountAmountInvalid", "Total discount amount cannot be negative.");

        public static Error BarcodeRequired =>
            Error.Validation("Sale.BarcodeRequired", "Barcode is required.");

        public static Error BatchNumberRequired =>
            Error.Validation("Sale.BatchNumberRequired", "Batch number is required.");

        public static Error ItemNameRequired =>
            Error.Validation("Sale.ItemNameRequired", "Item name is required.");

        public static Error InventoryBatchIdRequired =>
            Error.Validation("Sale.InventoryBatchIdRequired", "Inventory batch id is required.");

        public static Error ServiceIdRequired =>
            Error.Validation("Sale.ServiceIdRequired", "Service id is required for service lines.");
        public static Error OfflineLineQuantityMustBePositive =>
            Error.Validation("Sale.QuantityMustBePositive", "Quantity must be greater than zero.");

        public static Error OfflineLineAmountsInvalid =>
            Error.Validation("Sale.LineAmountsInvalid", "Line amounts cannot be negative.");

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

        public static Error ReturnLineTypeMismatch(Guid saleItemId) =>
            Error.Validation("SaleReturn.LineTypeMismatch", $"Return line type for sale item '{saleItemId}' does not match original sale line type.");

        public static Error ReturnServiceMustBeRefundOnly(Guid saleItemId) =>
            Error.Validation("SaleReturn.ServiceRefundOnly", $"Service sale item '{saleItemId}' supports refund-only returns.");

        public static Error ReturnGoodsConditionInvalid(Guid saleItemId) =>
            Error.Validation("SaleReturn.GoodsConditionInvalid", $"Goods sale item '{saleItemId}' must be marked as restockable or wastage.");

        public static Error ReturnNotFound(Guid saleReturnId) =>
            Error.NotFound("SaleReturn.NotFound", $"Sale return '{saleReturnId}' was not found.");

        public static Error ReturnAlreadyVoided =>
            Error.Validation("SaleReturn.AlreadyVoided", "Sale return is already voided.");

        public static Error ReturnVoidReasonRequired =>
            Error.Validation("SaleReturn.VoidReasonRequired", "Void reason is required.");

        public static Error ReturnVoidInsufficientStock =>
            Error.Conflict("SaleReturn.VoidInsufficientStock", "Void would make returned stock negative.");

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

        public static Error ReturnPayoutDestinationRequired =>
            Error.Validation("SaleReturn.PayoutDestinationRequired", "Payout destination is required when payout amount is greater than zero.");

        public static Error ReturnPayoutDestinationInvalid =>
            Error.Validation("SaleReturn.PayoutDestinationInvalid", "Return payout destination must be a recognised value.");

        public static Error ReturnCustomerDueNotSupported =>
            Error.Validation("SaleReturn.CustomerDueNotSupported", "Customer due returns are not supported yet.");

        public static Error ReturnNoteRequired(string reason) =>
            Error.Validation("SaleReturn.NoteRequired", $"Return line note is required for {reason}.");

        public static Error ReturnDueOverrideReasonRequired =>
            Error.Validation("SaleReturn.DueOverrideReasonRequired", "Due override reason is required when payout happens while customer due remains.");

        public static Error ReturnDueReductionExceedsOutstandingDue =>
            Error.Validation("SaleReturn.DueReductionExceedsOutstandingDue", "Due reduction cannot exceed current outstanding due.");

        public static Error ReturnRefundOverrideReasonRequired =>
            Error.Validation("SaleReturn.RefundOverrideReasonRequired", "Refund override reason is required when approving a refund above the discounted paid amount.");

        public static Error ReturnCreditNoteHasRedemptions =>
            Error.Conflict("SaleReturn.CreditNoteHasRedemptions", "Cannot void return because its credit note has already been redeemed.");

        public static Error ReturnInventoryAggregateNotFound(Guid itemId) =>
            Error.NotFound("SaleReturn.InventoryAggregateNotFound", $"Inventory aggregate was not found for item '{itemId}'.");

        public static Error InvalidPricingInput(Guid inventoryBatchId) =>
            Error.Validation("SalePricing.InvalidInput", $"Invalid pricing input for batch '{inventoryBatchId}'.");

        public static Error QuantityMustBePositive(Guid inventoryBatchId) =>
            Error.Validation("SalePricing.QuantityMustBePositive", $"Quantity for batch '{inventoryBatchId}' must be greater than zero.");

        public static Error InvalidDiscount(Guid inventoryBatchId) =>
            Error.Validation("SalePricing.InvalidDiscount", $"Invalid discount input for batch '{inventoryBatchId}'.");

        public static Error ItemDiscountWouldBeBelowCost(Guid inventoryBatchId) =>
            Error.Validation("SalePricing.ItemDiscountBelowCost", $"Item discount would make batch '{inventoryBatchId}' sell below cost.");

        public static Error LineWouldBeBelowCost(Guid inventoryBatchId) =>
            Error.Validation("SalePricing.BelowCost", $"Pricing would make batch '{inventoryBatchId}' sell below cost.");

        public static readonly Error SaleDiscountWouldBeBelowCost =
            Error.Validation("SalePricing.SaleDiscountBelowCost", "Sale discount would make one or more lines sell below cost.");

        public static readonly Error NoEligibleLinesForSaleDiscount =
            Error.Validation("SalePricing.NoEligibleLines", "No eligible lines available for sale-level discount.");

        public static readonly Error InvalidSaleDiscount =
            Error.Validation("SalePricing.InvalidSaleDiscount", "Invalid sale discount input.");
    }
}
