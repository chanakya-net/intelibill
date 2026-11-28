using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class ShopSetup
    {
        public static Error AggregateStockInsufficient =>
            Error.Conflict("ShopSetup.AggregateStockInsufficient", "The item's stock summary cannot cover this quantity.");

        public static Error Forbidden =>
            Error.Forbidden("ShopSetup.Forbidden", "An active membership in the requested shop is required.");

        public static Error InvoiceSeriesInvalid =>
            Error.Validation("ShopSetup.InvoiceSeriesInvalid", "Choose a valid invoice series.");

        public static Error OwnerRequired =>
            Error.Forbidden("ShopSetup.OwnerRequired", "Only an active owner may save shop setup preferences.");

        public static Error SampleUnavailable =>
            Error.Conflict("ShopSetup.SampleUnavailable", "Complete the current shop setup requirements to preview a sample receipt.");

        public static Error UnitReviewRequired =>
            Error.Conflict("ShopSetup.UnitReviewRequired", "Review the item's unit and quantity precision.");

        public static Error VersionConflict =>
            Error.Conflict("ShopSetup.VersionConflict", "Setup preferences changed. Review the latest settings and try again.");
    }
}
