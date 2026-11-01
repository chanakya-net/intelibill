using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class PurchaseReceipt
    {
        public static Error NotFound =>
            Error.NotFound("PurchaseReceipt.NotFound", "Purchase receipt was not found.");

        public static Error EvidenceMissing =>
            Error.Validation("PurchaseReceipt.EvidenceMissing", "The original total of this receipt is not on record, so its cost cannot be corrected. Record the difference with the supplier as a separate adjustment.");

        public static Error VersionConflict =>
            Error.Conflict("PurchaseReceipt.VersionConflict", "This receipt changed after it was loaded. Refresh it and enter the correction again.");

        public static Error VersionRequired =>
            Error.Validation("PurchaseReceipt.VersionRequired", "The receipt version the correction was entered against is required.");

        public static Error CostUnchanged =>
            Error.Validation("PurchaseReceipt.CostUnchanged", "The revised total equals the current receipt total.");

        public static Error TotalCostInvalid =>
            Error.Validation("PurchaseReceipt.TotalCostInvalid", "Receipt total must be between 0 and 99,999,999.99 with at most two decimals.");

        public static Error ReasonRequired =>
            Error.Validation("PurchaseReceipt.ReasonRequired", "A reason is required for a receipt cost correction.");

        public static Error ReasonTooLong =>
            Error.Validation("PurchaseReceipt.ReasonTooLong", "Reason cannot exceed 500 characters.");

        public static Error BatchVoided =>
            Error.Validation("PurchaseReceipt.BatchVoided", "The receipt's batch is voided; its payable was already reversed.");

        public static Error SupplierMissing =>
            Error.Validation("PurchaseReceipt.SupplierMissing", "The receipt has no supplier to post the correction to.");
    }
}
