using ErrorOr;

namespace Intelibill.Application.Common.Errors;

/// <summary>
/// Centralised domain error definitions. Add static inner classes per aggregate.
/// </summary>
public static partial class Errors
{
    public static class General
    {
        public static Error NotFound(string resource, Guid id) =>
            Error.NotFound($"{resource}.NotFound", $"{resource} with id '{id}' was not found.");

        public static Error Conflict(string description) =>
            Error.Conflict("General.Conflict", description);

        public static Error Unexpected(string description = "An unexpected error occurred.") =>
            Error.Unexpected("General.Unexpected", description);
    }

    public static class Dashboard
    {
        public static readonly Error InvalidDateRange =
            Error.Validation("Dashboard.InvalidDateRange", "Start date must be on or before end date.");

        public static readonly Error RangeExceeds90Days =
            Error.Validation("Dashboard.RangeExceeds90Days", "Date range must not exceed 90 days.");

        public static readonly Error FutureDateNotAllowed =
            Error.Validation("Dashboard.FutureDateNotAllowed", "End date must not be in the future.");
    }
}
