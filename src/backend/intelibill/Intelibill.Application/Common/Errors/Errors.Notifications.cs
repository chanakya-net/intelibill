using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Notifications
    {
        public static Error RecipientAuthorityInvalid =>
            Error.Validation("Notifications.RecipientAuthorityInvalid", "The notice recipient verification request is invalid.");

        public static Error RecipientAuthorityUnauthorized =>
            Error.Unauthorized("Notifications.RecipientAuthorityUnauthorized", "The notice recipient verification assertion is invalid.");

        public static Error RecipientAuthorityUnavailable =>
            Error.Failure("Notifications.RecipientAuthorityUnavailable", "Notice recipient authority is temporarily unavailable.");

        public static Error ContactInvalid =>
            Error.Validation("Notifications.ContactInvalid", "Enter a valid billing contact email address and operation key.");

        public static Error ContactNotFound =>
            Error.NotFound("Notifications.ContactNotFound", "No matching billing contact was found.");

        public static Error ContactConflict =>
            Error.Conflict("Notifications.ContactConflict", "An active billing contact with that destination or identity already exists.");

        public static Error ContactProofInvalid =>
            Error.Forbidden(
                "Notifications.ContactProofInvalid",
                "The billing contact verification proof is invalid, expired, or already used.");

        public static Error ContactOperationMismatch =>
            Error.Conflict("Notifications.ContactOperationMismatch", "This operation key was already used with different contact details.");

        public static Error VerifiedEmailRequired =>
            Error.Forbidden(
                "Notifications.VerifiedEmailRequired",
                "Verify your email address before opting in as a commercial billing contact.");
    }
}
