using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Inventory
    {
        public static Error ItemNameRequired =>
            Error.Validation("Inventory.ItemNameRequired", "Item name is required.");

        public static Error BarcodeRequired =>
            Error.Validation("Inventory.BarcodeRequired", "Barcode is required.");

        public static Error SearchTermRequired =>
            Error.Validation("Inventory.SearchTermRequired", "Search term is required.");

        public static Error UomRequired =>
            Error.Validation("Inventory.UomRequired", "UOM is required.");

        public static Error BatchNumberRequired =>
            Error.Validation("Inventory.BatchNumberRequired", "Batch number is required.");

        public static Error QuantityMustBePositive =>
            Error.Validation("Inventory.QuantityMustBePositive", "Quantity must be greater than zero.");

        public static Error UserIsNotOwnerOrManager =>
            Error.Forbidden("Inventory.UserIsNotOwnerOrManager", "Only owner or manager can add inventory.");

        public static Error UserIsNotOwner =>
            Error.Forbidden("Inventory.UserIsNotOwner", "Only shop owner can perform this operation.");

        public static Error ItemIdentityConflict =>
            Error.Validation("Inventory.ItemIdentityConflict", "Provided name and barcode refer to different items.");

        public static Error ItemNameBarcodeMismatch =>
            Error.Validation("Inventory.ItemNameBarcodeMismatch", "Provided name and barcode do not match the existing item.");

        public static Error BatchNotFound =>
            Error.NotFound("Inventory.BatchNotFound", "Inventory batch was not found.");

        public static Error BatchAlreadyVoided =>
            Error.Validation("Inventory.BatchAlreadyVoided", "Inventory batch is already voided.");

        public static Error BatchNumberAlreadyExists =>
            Error.Conflict("Inventory.BatchNumberAlreadyExists", "An active batch with this number already exists for this item.");

        public static Error InventoryAggregateNotFound =>
            Error.NotFound("Inventory.InventoryAggregateNotFound", "Inventory aggregate was not found for this item.");

        public static Error SupplierLedgerEntryInvalid =>
            Error.Validation("Inventory.SupplierLedgerEntryInvalid", "Supplier ledger entry is invalid.");

        public static Error AdjustmentQuantityScaleInvalid =>
            Error.Validation("Inventory.AdjustmentQuantityScaleInvalid", "Adjustment quantity cannot have more than two decimal places.");

        public static Error AdjustmentPerformedAtInFuture =>
            Error.Validation("Inventory.AdjustmentPerformedAtInFuture", "Adjustment performed timestamp cannot be in the future.");

        public static Error AdjustmentReasonDirectionMismatch =>
            Error.Validation("Inventory.AdjustmentReasonDirectionMismatch", "Adjustment reason is not valid for the direction.");

        public static Error AdjustmentNotesRequired =>
            Error.Validation("Inventory.AdjustmentNotesRequired", "Notes are required for other loss or other gain adjustments.");

        public static Error AdjustmentQuantityExceedsBatchQuantity =>
            Error.Validation("Inventory.AdjustmentQuantityExceedsBatchQuantity", "Adjustment quantity cannot exceed the current batch quantity.");

        public static Error AdjustmentNotFound =>
            Error.NotFound("Inventory.AdjustmentNotFound", "Inventory adjustment was not found.");

        public static Error AdjustmentAlreadyVoided =>
            Error.Validation("Inventory.AdjustmentAlreadyVoided", "Inventory adjustment is already voided.");

        public static Error AdjustmentVoidReasonRequired =>
            Error.Validation("Inventory.AdjustmentVoidReasonRequired", "Void reason is required.");

        public static Error ClientRowIdInvalid =>
            Error.Validation("Inventory.ClientRowIdInvalid", "Every row needs a client row id of 1 to 64 characters.");

        public static Error DuplicateClientRowId =>
            Error.Validation("Inventory.DuplicateClientRowId", "Client row ids must be unique within a batch.");

        public static Error BatchOperationKeyInvalid =>
            Error.Validation("Inventory.BatchOperationKeyInvalid", "Operation key must be 1 to 60 characters.");

        public static Error BatchVersionConflict =>
            Error.Conflict("Inventory.BatchVersionConflict", "This batch changed after it was loaded. Refresh it, count again and submit the new count.");

        public static Error BatchConsumed =>
            Error.Conflict("Inventory.BatchConsumed", "This receipt has already been sold from or adjusted, so it cannot be voided. Count the remaining stock to remove it, or record a supplier return.");

        public static Error StockCountReasonRequired =>
            Error.Validation("Inventory.StockCountReasonRequired", "A reason is required for a stock count.");

        public static Error StockCountQuantityInvalid =>
            Error.Validation("Inventory.StockCountQuantityInvalid", "Counted quantity must be zero or more, with at most three decimal places.");

        public static Error StockCountReasonTooLong =>
            Error.Validation("Inventory.StockCountReasonTooLong", "Reason cannot exceed 500 characters.");

        public static Error ConcurrentChange =>
            Error.Conflict("Inventory.ConcurrentChange", "This stock changed while the request was being saved. Nothing was saved; refresh and try again.");

        public static Error RowRetryable =>
            Error.Failure("Inventory.RowRetryable", "This row was not saved because of a temporary problem. Send the same batch again to retry it.");
    }
}
