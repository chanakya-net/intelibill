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

        public static Error InvalidLineQuantity =>
            Error.Validation("PurchaseOrder.InvalidLineQuantity", "Expected quantity must be greater than zero.");

        public static Error InvalidLineUnitCost =>
            Error.Validation("PurchaseOrder.InvalidLineUnitCost", "Unit cost cannot be negative.");

        public static Error DuplicateItem =>
            Error.Validation("PurchaseOrder.DuplicateItem", "Purchase order line items must be unique.");

        public static Error InvalidPageSize =>
            Error.Validation("PurchaseOrder.InvalidPageSize", "Page size must be between 1 and 100.");

        public static Error SequenceError =>
            Error.Unexpected("PurchaseOrder.SequenceError", "Failed to generate purchase order number.");
    }
}
