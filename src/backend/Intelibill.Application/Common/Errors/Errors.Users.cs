using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Users
    {
        public static Error RoleNotSupported =>
            Error.Validation("Users.RoleNotSupported", "Role must be either Manager or Staff.");

        public static Error UserAlreadyMember =>
            Error.Conflict("Users.UserAlreadyMember", "This user is already a member of the active shop.");

        public static Error CannotModifyOwner =>
            Error.Forbidden("Users.CannotModifyOwner", "Owner accounts cannot be modified from this endpoint.");

        public static Error CannotModifyOwnMembership =>
            Error.Forbidden("Users.CannotModifyOwnMembership", "You cannot change your own membership from this endpoint.");

        public static Error MembershipStatusNotSupported =>
            Error.Validation("Users.MembershipStatusNotSupported", "Status must be Active, Suspended or Removed.");

        public static Error MembershipVersionConflict =>
            Error.Conflict("Users.MembershipVersionConflict", "This membership changed while you were editing it. Reload and try again.");

        public static Error MembershipNotFound =>
            Error.NotFound("Users.MembershipNotFound", "This person is not a member of this shop.");

        /// <summary>
        /// The pre-invitation payload edited the global account. It is refused explicitly rather
        /// than partially applied, so an old client cannot silently keep cross-tenant control.
        /// </summary>
        public static Error MembershipOperationRequired =>
            Error.Conflict(
                "Users.MembershipOperationRequired",
                "Shop administration changes a membership, not an account. Use the shop member role and status endpoint; the account holder changes their own sign-in details.");

        public static Error InvitationNotFound =>
            Error.NotFound("Users.InvitationNotFound", "This invitation is no longer available.");

        public static Error InvitationNotAcceptable =>
            Error.Validation("Users.InvitationNotAcceptable", "This invitation is expired, already used, or has been withdrawn.");

        public static Error InvitationWrongAccount =>
            Error.Forbidden("Users.InvitationWrongAccount", "This invitation was sent to a different verified email address.");

        public static Error InvitationRequiresVerifiedEmail =>
            Error.Forbidden("Users.InvitationRequiresVerifiedEmail", "Verify your email address before accepting a shop invitation.");

        public static Error InvitationBudgetExhausted =>
            Error.Conflict("Users.InvitationBudgetExhausted", "Too many invitations were sent for this shop recently. Try again later.");

        public static Error InvitationAlreadyMember =>
            Error.Conflict("Users.InvitationAlreadyMember", "That email address already has an active membership in this shop.");
    }
}
