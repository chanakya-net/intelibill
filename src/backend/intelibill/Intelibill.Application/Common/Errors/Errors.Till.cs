using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Till
    {
        public static Error ReviewNotRequired => Error.Conflict("Till.ReviewNotRequired", "The invoice belongs to its still open session. Use ordinary historical replay to include it in the drawer.");
        public static Error ReviewRequired => Error.Conflict("Till.ReviewRequired", "The frozen invoice requires explicit till reconciliation.");
        public static Error Disabled => Error.Conflict("Till.Disabled", "Till sessions are not enabled for this shop.");
        public static Error TenderRequired => Error.Validation("Till.TenderRequired", "A concrete known external tender is required.");
        public static Error MixedTenderUnsupported => Error.Validation("Till.MixedTenderUnsupported", "Exactly one external tender is supported.");
        public static Error SettlementEvidenceRequired => Error.Validation("Till.SettlementEvidenceRequired", "An explicit physical settlement disposition and evidence are required.");
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

        public static Error AlreadyOpen =>
            Error.Conflict("Till.AlreadyOpen", "This till already has an open session.");

        public static Error AlreadyReviewed =>
            Error.Conflict("Till.AlreadyReviewed", "This immutable close already has a review.");

        public static Error AmountInvalid =>
            Error.Validation("Till.AmountInvalid", "Expense amount must be positive INR with at most two decimals.");

        public static Error NameExists =>
            Error.Conflict("Till.NameExists", "A till with this name already exists.");

        public static Error ReasonRequired =>
            Error.Validation("Till.ReasonRequired", "Manual cash transfers require cash and a reason.");

        public static Error RefundConfirmationInvalid =>
            Error.Validation("Till.RefundConfirmationInvalid", "Confirm the actual external refund only when an external payout occurs.");

        public static Error SettingsBlocked =>
            Error.Conflict(
                "Till.SettingsBlocked",
                "Close and review all sessions and resolve known offline submissions before disabling till tracking.");

        public static Error SettingsConfirmationRequired =>
            Error.Validation("Till.SettingsConfirmationRequired", "Confirm the change and provide a reason of 1 to 500 characters.");

        public static Error SettingsUnavailable =>
            Error.Conflict("Till.SettingsUnavailable", "Till tracking is not available for this shop's deployment.");

        public static Error SettingsVersionConflict =>
            Error.Conflict("Till.SettingsVersionConflict", "Till setup changed. Refresh its current state before trying again.");

        public static Error SettlementDirectionInvalid =>
            Error.Validation("Till.SettlementDirectionInvalid", "Settlement must match the source's exact signed change.");

        public static Error SourceInvalid =>
            Error.Validation("Till.SourceInvalid", "A valid source, operation and signed settlement amounts are required.");

        public static Error TenderAmountMismatch =>
            Error.Validation("Till.TenderAmountMismatch", "Tender must equal the exact source settlement amount.");

        public static Error TenderReferenceInvalid =>
            Error.Validation("Till.TenderReferenceInvalid", "The bank account must belong to this shop.");
    }
}
