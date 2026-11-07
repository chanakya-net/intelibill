using ErrorOr;
namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class SaleCancellation
    {
        public static Error PostingDisabled => Error.Failure("SaleCancellation.PostingDisabled", "Sale cancellation posting is disabled for this shop.");
        public static Error Forbidden => Error.Forbidden("SaleCancellation.Forbidden", "Only an active owner or manager may cancel or preview a sale.");
        public static Error VersionConflict => Error.Conflict("SaleCancellation.VersionConflict", "The sale or stock changed. Refresh the preview and retry.");
        public static Error Busy => Error.Conflict("SaleCancellation.Busy", "The sale or stock is busy. Retry the operation.");
        public static Error AlreadyCancelled => Error.Conflict("SaleCancellation.AlreadyCancelled", "This sale has already been cancelled.");
    }
}
