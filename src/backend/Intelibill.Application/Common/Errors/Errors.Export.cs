using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Export
    {
        public static Error UserIsNotOwnerOrManager =>
            Error.Forbidden("Export.UserIsNotOwnerOrManager", "Only owner or manager can export sales data.");

        public static Error PdfRowLimitExceeded(int maxRows) =>
            Error.Validation(
                "Export.PdfRowLimitExceeded",
                $"PDF export supports up to {maxRows} rows. Please narrow the date range or export to Excel instead.");
    }
}
