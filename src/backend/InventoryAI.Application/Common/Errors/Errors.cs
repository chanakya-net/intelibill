using ErrorOr;

namespace InventoryAI.Application.Common.Errors;

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
}
