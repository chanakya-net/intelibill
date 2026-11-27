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
    }
}
