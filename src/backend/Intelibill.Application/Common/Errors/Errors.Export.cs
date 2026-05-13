using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Export
    {
        public static Error UserIsNotOwnerOrManager =>
            Error.Forbidden("Export.UserIsNotOwnerOrManager", "Only owner or manager can export sales data.");
    }
}
