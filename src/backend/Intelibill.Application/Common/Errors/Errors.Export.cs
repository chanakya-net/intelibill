using System.Globalization;
using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Export
    {
        public static Error UserIsNotOwnerOrManager =>
            Error.Forbidden("Export.UserIsNotOwnerOrManager", "Only owner or manager can export sales data.");

        public static Error PdfRowLimitExceeded(int maxRows, long? count = null, string? level = null, string? format = null) =>
            Error.Validation(
                "Export.PdfRowLimitExceeded",
                $"PDF export supports up to {maxRows} rows. Please narrow the date range or export to Excel instead.",
                new Dictionary<string, object> { ["limit"] = maxRows, ["count"] = count ?? 0, ["level"] = level ?? "summary", ["format"] = format ?? "pdf" });

        public static Error RowLimitExceeded(long limit, long count, string level, string format) =>
            Error.Validation("Export.RowLimitExceeded", $"This export contains {count} logical rows; {format} supports up to {limit}. Please narrow the date range.",
                new Dictionary<string, object> { ["limit"] = limit, ["count"] = count, ["level"] = level, ["format"] = format });

        public static Error ReadLimitExceeded(long limit, long count, string level, string format) =>
            Error.Validation("Export.ReadLimitExceeded", $"This export needs {count} source records; the limit is {limit}. Please narrow the date range.",
                new Dictionary<string, object> { ["limit"] = limit, ["count"] = count, ["level"] = level, ["format"] = format });

        public static Error ReturnNeedsAccountingReview(string returnNumber, string invoiceNumber, string reason) =>
            Error.Conflict(
                "Export.ReturnNeedsAccountingReview",
                $"Return {returnNumber} on invoice {invoiceNumber} needs accounting review before it can be exported to Tally: {reason} Export a period that excludes it, or review it with your accountant.");

        public static Error VoucherUnbalanced(string voucherNumber, decimal debits, decimal credits) =>
            Error.Conflict(
                "Export.VoucherUnbalanced",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Voucher {voucherNumber} does not balance: debits {debits:F2}, credits {credits:F2}. It was not exported."));

        public static Error VoucherInvalid(string voucherNumber, string reason) =>
            Error.Conflict(
                "Export.VoucherInvalid",
                $"Voucher {voucherNumber} cannot be exported: {reason}.");
    }
}
