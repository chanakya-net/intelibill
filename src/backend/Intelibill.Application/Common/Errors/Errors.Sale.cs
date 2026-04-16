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
    }
}
