using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Operation
    {
        public static Error PayloadMismatch =>
            Error.Conflict("Operation.PayloadMismatch", "Operation key has already been used with different request content.");

        public static Error InvalidKey =>
            Error.Validation("Operation.InvalidKey", "Operation key must be 1 to 128 characters.");

        public static Error NotFound =>
            Error.NotFound("Operation.NotFound", "No committed operation is visible for this key.");

        public static Error UnknownType =>
            Error.NotFound("Operation.UnknownType", "Operation type is not recognised.");

        public static Error UserIsNotOwnerOrManager =>
            Error.Forbidden("Operation.UserIsNotOwnerOrManager", "Only owner or manager can view financial operations.");
    }
}
