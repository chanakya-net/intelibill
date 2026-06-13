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
    }
}
