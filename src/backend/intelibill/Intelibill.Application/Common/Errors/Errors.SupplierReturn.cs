using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class SupplierReturn
    {
        public static Error NotFound =>
            Error.NotFound("SupplierReturn.NotFound", "Supplier return was not found.");

        public static Error SourceIneligible =>
            Error.Conflict(
                "SupplierReturn.SourceIneligible",
                "Completion requires an eligible receipt or shipment already recorded for this shop.");
    }
}
