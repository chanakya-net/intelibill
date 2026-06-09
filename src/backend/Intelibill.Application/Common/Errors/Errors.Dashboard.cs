using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Dashboard
    {
        public static Error PartialDateRange =>
            Error.Validation("Dashboard.PartialDateRange", "Both 'from' and 'to' dates must be provided together.");

        public static Error InvalidDateRange =>
            Error.Validation("Dashboard.InvalidDateRange", "The 'from' date must be on or before the 'to' date.");

        public static Error DateRangeTooLarge =>
            Error.Validation("Dashboard.DateRangeTooLarge", "The date range cannot exceed 90 days.");

        public static Error UserIsNotOwnerOrManager =>
            Error.Forbidden("Dashboard.UserIsNotOwnerOrManager", "Only owner or manager can access dashboard data.");
    }
}
