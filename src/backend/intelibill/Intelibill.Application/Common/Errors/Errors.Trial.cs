using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Trial
    {
        public static Error AcceptanceRequired =>
            Error.Validation("Trial.AcceptanceRequired", "Confirm the exact trial terms to start a trial.");

        public static Error OperationNotFound =>
            Error.NotFound("Trial.OperationNotFound", "The trial operation was not found.");

        public static Error OperationPending =>
            Error.Conflict("Trial.OperationPending", "A trial activation is already pending for this shop.");

        public static Error OwnerRequired =>
            Error.Forbidden("Trial.OwnerRequired", "Only the active shop's owner can start or recover its trial.");
    }
}
