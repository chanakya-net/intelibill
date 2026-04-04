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

        public static Error UserIsNotOwnerOrManager =>
            Error.Forbidden("Item.UserIsNotOwnerOrManager", "Only owner or manager can add items.");
    }
}