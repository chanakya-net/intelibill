using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Till
    {
        public static Error Disabled => Error.Conflict("Till.Disabled", "Till sessions are not enabled for this shop.");
        public static Error SessionRequired => Error.Validation("Till.SessionRequired", "Select an explicit open till session.");
        public static Error NotFound => Error.NotFound("Till.NotFound", "The till or session was not found in this shop.");
        public static Error Busy => Error.Conflict("Till.Busy", "The till is busy. Retry the same operation.");
        public static Error VersionConflict => Error.Conflict("Till.VersionConflict", "The session changed. Refresh before continuing.");
        public static Error SessionNotFound => Error.NotFound("Till.SessionNotFound", "The till session was not found in this shop.");
        public static Error SessionClosed => Error.Conflict("Till.SessionClosed", "The till session is closed.");
        public static Error Closed => SessionClosed;
        public static Error CashierMismatch => Error.Forbidden("Till.CashierMismatch", "Only the assigned cashier or an authorized manager may operate this session.");
        public static Error Overdue => Error.Conflict("Till.Overdue", "The till session's business date has ended.");
        public static Error BusinessDateMismatch => Error.Validation("Till.BusinessDateMismatch", "The source must use the session's current business date.");
        public static Error DateMismatch => BusinessDateMismatch;
        public static Error Forbidden => Error.Forbidden("Till.Forbidden", "An active authorized shop membership is required.");
        public static Error OverrideReasonRequired => Error.Validation("Till.OverrideReasonRequired", "Acting for another cashier requires a reason.");
        public static Error DuplicateSource => Error.Conflict("Till.DuplicateSource", "This source has already been attributed to a till session.");
    }
}
