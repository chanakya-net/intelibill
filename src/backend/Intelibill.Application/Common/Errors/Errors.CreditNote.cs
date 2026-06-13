using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class CreditNote
    {
        public static Error CreditNoteNotFound(string code) =>
            Error.NotFound("CreditNote.NotFound", $"Credit note '{code}' was not found.");

        public static Error UserIsNotOwnerManagerOrStaff =>
            Error.Forbidden("CreditNote.UserIsNotOwnerManagerOrStaff", "Only owner, manager, or staff can verify credit notes.");

        public static Error CreditNoteExpired =>
            Error.Conflict("CreditNote.Expired", "Credit note is expired.");

        public static Error CreditNoteVoided =>
            Error.Conflict("CreditNote.Voided", "Credit note is voided.");

        public static Error CreditNoteUnusable =>
            Error.Conflict("CreditNote.Unusable", "Credit note cannot be used.");
    }
}
