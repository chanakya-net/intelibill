using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class PurchaseOrder
    {
        public static Error NotFound =>
            Error.NotFound("PurchaseOrder.NotFound", "Purchase order was not found.");

        public static Error UserCannotCreatePurchaseOrder =>
            Error.Forbidden("PurchaseOrder.UserCannotCreatePurchaseOrder", "Only owner or manager can create purchase orders.");

        public static Error LineDescriptionRequired =>
            Error.Validation("PurchaseOrder.LineDescriptionRequired", "Line description is required.");

        public static Error LineItemRequired =>
            Error.Validation("PurchaseOrder.LineItemRequired", "Line item is required.");

        public static Error LineItemNotFound =>
            Error.Validation("PurchaseOrder.LineItemNotFound", "Line item does not belong to the active shop.");

        public static Error InvalidLineQuantity =>
            Error.Validation("PurchaseOrder.InvalidLineQuantity", "Expected quantity must be greater than zero.");

        public static Error InvalidLineUnitCost =>
            Error.Validation("PurchaseOrder.InvalidLineUnitCost", "Unit cost cannot be negative.");

        public static Error DuplicateItem =>
            Error.Validation("PurchaseOrder.DuplicateItem", "Purchase order line items must be unique.");

        public static Error InvalidPageSize =>
            Error.Validation("PurchaseOrder.InvalidPageSize", "Page size must be between 1 and 100.");

        public static Error InvalidOrderDateRange =>
            Error.Validation("PurchaseOrder.InvalidOrderDateRange", "Order date from must be on or before order date to.");

        public static Error SequenceError =>
            Error.Unexpected("PurchaseOrder.SequenceError", "Failed to generate purchase order number.");

        public static Error CannotUpdateNonDraft =>
            Error.Validation("PurchaseOrder.CannotUpdateNonDraft", "Only draft purchase orders can be updated.");

        public static Error SupplierRequired =>
            Error.Validation("PurchaseOrder.SupplierRequired", "A supplier is required to place a purchase order.");

        public static Error SupplierInvalidForPlacement =>
            Error.Validation("PurchaseOrder.SupplierInvalidForPlacement", "Supplier must be active, non-system, and belong to this shop.");

        public static Error AtLeastOneLineRequired =>
            Error.Validation("PurchaseOrder.AtLeastOneLineRequired", "At least one line is required to place a purchase order.");

        public static Error CannotPlaceNonDraft =>
            Error.Validation("PurchaseOrder.CannotPlaceNonDraft", "Only draft purchase orders can be placed.");

        public static Error CannotDeleteNonDraft =>
            Error.Validation("PurchaseOrder.CannotDeleteNonDraft", "Only draft purchase orders can be deleted.");

        public static Error CannotCancelAfterReceipt =>
            Error.Validation("PurchaseOrder.CannotCancelAfterReceipt", "Cannot cancel a purchase order that has received items.");

        public static Error CannotCancelInvalidStatus =>
            Error.Validation("PurchaseOrder.CannotCancelInvalidStatus", "Only placed purchase orders can be cancelled.");

        public static Error CancellationReasonRequired =>
            Error.Validation("PurchaseOrder.CancellationReasonRequired", "Cancellation reason is required.");

        public static Error UserCannotMutatePurchaseOrder =>
            Error.Forbidden("PurchaseOrder.UserCannotMutatePurchaseOrder", "Only owner or manager can perform this action.");

        public static Error CannotReceiveInvalidStatus =>
            Error.Validation("PurchaseOrder.CannotReceiveInvalidStatus", "Only placed or partially received purchase orders can be received.");

        public static Error ReceiptLineRequired =>
            Error.Validation("PurchaseOrder.ReceiptLineRequired", "Exactly one receipt line is required.");

        public static Error ReceiptLineNotFound =>
            Error.Validation("PurchaseOrder.ReceiptLineNotFound", "Purchase order receipt line was not found.");

        public static Error ReceiptQuantityInvalid =>
            Error.Validation("PurchaseOrder.ReceiptQuantityInvalid", "Receipt quantity must be greater than zero.");

        public static Error ReceiptQuantityOverRemaining =>
            Error.Validation("PurchaseOrder.ReceiptQuantityOverRemaining", "Receipt quantity cannot exceed remaining purchase order quantity.");
    }
}
