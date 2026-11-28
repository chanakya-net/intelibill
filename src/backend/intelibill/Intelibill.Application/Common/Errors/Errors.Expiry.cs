using ErrorOr;
namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Expiry
    {
        public static Error OwnerRequired => Error.Forbidden("Expiry.OwnerRequired", "Only an active owner can change expiry policies.");
        public static Error MembershipRequired => Error.Forbidden("Expiry.MembershipRequired", "Active shop membership is required.");
        public static Error ManagerRequired => Error.Forbidden("Expiry.ManagerRequired", "Only an owner or manager can view expiry readiness.");
        public static Error PolicyBusy => Error.Conflict("Expiry.PolicyBusy", "Expiry policy is busy; try again.");
        public static Error PolicyVersionConflict => Error.Conflict("Expiry.PolicyVersionConflict", "Expiry policy changed; refresh before saving.");
        public static Error HistoryUnknown => Error.Conflict("Expiry.HistoryUnknown", "Retained expiry policy history is missing or invalid.");
        public static Error PolicyInvalid => Error.Validation("Expiry.PolicyInvalid", "A valid mode and warning window of 0 to 365 days are required.");
        public static Error ReasonRequired => Error.Validation("Expiry.ReasonRequired", "A reason of 1 to 500 characters is required.");
        public static Error PageInvalid => Error.Validation("Expiry.PageInvalid", "Page must be positive and page size between 1 and 100.");
        public static Error ItemNotFound => Error.NotFound("Expiry.ItemNotFound", "The item does not exist in this shop.");

        public static Error ApprovalInvalid =>
            Error.Validation("Expiry.ApprovalInvalid", "Expiry approvals must name each required lot exactly once.");

        public static Error ApprovalRoleRequired =>
            Error.Forbidden("Expiry.ApprovalRoleRequired", "An active owner or manager must approve the warning in their own session.");

        public static Error ApprovalStale =>
            Error.Conflict("Expiry.ApprovalStale", "Lot quantity or expiry facts changed; refresh and approve again.");

        public static Error DisposalCannotBeVoided =>
            Error.Conflict("Expiry.DisposalCannotBeVoided", "Disposed quarantine cannot be resurrected.");

        public static Error DispositionInvalid =>
            Error.Validation(
                "Expiry.DispositionInvalid",
                "Supply an operation key, version, root, positive quantity, action and 1 to 500 character reason.");

        public static Error InvalidDateOrder =>
            Error.Validation("Expiry.InvalidDateOrder", "Manufacturing date must not follow expiry date.");

        public static Error QuantityPrecisionExceeded =>
            Error.Validation("Expiry.QuantityPrecisionExceeded", "Quantity exceeds the item's scale.");

        public static Error QuarantineProtected =>
            Error.Conflict("Expiry.QuarantineProtected", "Count cannot reduce physical stock below quarantine.");

        public static Error Quarantined =>
            Error.Conflict("Expiry.Quarantined", "All stock in this lot is quarantined.");

        public static Error RequiredDateMissing =>
            Error.Validation("Expiry.RequiredDateMissing", "This item requires an expiry date.");

        public static Error ReviewRequired =>
            Error.Conflict("Expiry.ReviewRequired", "The issued invoice needs trusted expiry evidence review.");
    }
}
