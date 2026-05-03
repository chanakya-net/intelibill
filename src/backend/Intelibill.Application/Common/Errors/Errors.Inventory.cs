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
    }
}
