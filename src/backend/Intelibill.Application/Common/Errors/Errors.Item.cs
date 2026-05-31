using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Item
    {
        public static Error NameRequired =>
            Error.Validation("Item.NameRequired", "Item name is required.");

        public static Error BarcodeRequired =>
            Error.Validation("Item.BarcodeRequired", "Barcode is required.");

        public static Error UomRequired =>
            Error.Validation("Item.UomRequired", "UOM is required.");

        public static Error BarcodeAlreadyExists =>
            Error.Conflict("Item.BarcodeAlreadyExists", "An item with this barcode already exists in the active shop.");

        public static Error NameAlreadyExists =>
            Error.Conflict("Item.NameAlreadyExists", "An item with this name already exists in the active shop.");

        public static Error BarcodeGenerationFailed =>
            Error.Unexpected("Item.BarcodeGenerationFailed", "Unable to generate a unique item barcode.");

        public static Error UserIsNotOwnerOrManager =>
            Error.Forbidden("Item.UserIsNotOwnerOrManager", "Only owner or manager can add items.");

        public static Error ItemNotFound =>
            Error.NotFound("Item.ItemNotFound", "Item not found.");

        public static Error BarcodeLabelItemsRequired =>
            Error.Validation("Item.BarcodeLabelItemsRequired", "At least one item is required for barcode label printing.");

        public static Error BarcodeLabelItemIdRequired =>
            Error.Validation("Item.BarcodeLabelItemIdRequired", "Item id is required for barcode label printing.");

        public static Error BarcodeLabelQuantityInvalid =>
            Error.Validation("Item.BarcodeLabelQuantityInvalid", "Label quantity must be greater than zero.");

        public static Error BarcodeLabelQuantityLimitExceeded(int maxQuantity) =>
            Error.Validation("Item.BarcodeLabelQuantityLimitExceeded", $"Total label quantity cannot exceed {maxQuantity}.");

        public static Error BarcodeLabelItemNotFound(Guid itemId) =>
            Error.Validation("Item.BarcodeLabelItemNotFound", $"Item '{itemId}' was not found in the active shop.");

        public static Error BarcodeLabelBatchNotFound(Guid itemId, Guid batchId) =>
            Error.Validation("Item.BarcodeLabelBatchNotFound", $"Batch '{batchId}' for item '{itemId}' was not found in the active shop.");
    }
}
