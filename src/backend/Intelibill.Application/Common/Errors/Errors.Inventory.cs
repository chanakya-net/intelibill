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

        public static Error UomRequired =>
            Error.Validation("Inventory.UomRequired", "UOM is required.");

        public static Error BatchNumberRequired =>
            Error.Validation("Inventory.BatchNumberRequired", "Batch number is required.");

        public static Error QuantityMustBePositive =>
            Error.Validation("Inventory.QuantityMustBePositive", "Quantity must be greater than zero.");

        public static Error UserIsNotOwnerOrManager =>
            Error.Forbidden("Inventory.UserIsNotOwnerOrManager", "Only owner or manager can add inventory.");

        public static Error ItemIdentityConflict =>
            Error.Validation("Inventory.ItemIdentityConflict", "Provided name and barcode refer to different items.");

        public static Error ItemNameBarcodeMismatch =>
            Error.Validation("Inventory.ItemNameBarcodeMismatch", "Provided name and barcode do not match the existing item.");
    }
}