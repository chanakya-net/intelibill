using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Billing
    {
        public static Error AccountAccessDenied =>
            Error.Forbidden("Billing.AccountAccessDenied", "Billing account access was denied.");

        public static Error AffiliationConflict =>
            Error.Conflict("Billing.AffiliationConflict", "The affiliation selection is no longer available.");

        public static Error AffiliationInvalid =>
            Error.Validation("Billing.AffiliationInvalid", "Select a shop and one coverage choice.");

        public static Error AffiliationMissing =>
            Error.NotFound("Billing.AffiliationMissing", "The affiliation request was not found.");

        public static Error CancellationInvalid =>
            Error.Validation("Billing.CancellationInvalid", "Confirm the current term end and affected shops.");

        public static Error CatalogueCursorInvalid =>
            Error.Validation("Billing.CatalogueCursorInvalid", "The catalogue position is invalid.");

        public static Error ConsentInvalid =>
            Error.Conflict("Billing.ConsentInvalid", "A current, unrevoked consent is required.");

        public static Error DelegationUnavailable =>
            Error.Failure("Billing.DelegationUnavailable", "Billing access is temporarily unavailable.");

        public static Error InvitationRecipientInvalid =>
            Error.Validation("Billing.InvitationRecipientInvalid", "Enter a valid email or phone number.");

        public static Error InvitationRevisionConflict =>
            Error.Conflict("Billing.InvitationRevisionConflict", "Billing membership changed. Reload and try again.");

        public static Error InvitationUnavailable =>
            Error.NotFound("Billing.InvitationUnavailable", "This billing invitation is unavailable.");

        public static Error MemberRemovalInvalid =>
            Error.Validation("Billing.MemberRemovalInvalid", "Enter valid removal details and a reason.");

        public static Error OperationKeyInvalid =>
            Error.Validation("Billing.OperationKeyInvalid", "The operation key is invalid.");

        public static Error OperationPayloadMismatch =>
            Error.Conflict("Billing.OperationPayloadMismatch", "This operation key was already used with different details.");

        public static Error OwnershipTransferConflict =>
            Error.Conflict("Billing.OwnershipTransferConflict", "The billing authority or transfer has changed.");

        public static Error OwnershipTransferInvalid =>
            Error.Validation("Billing.OwnershipTransferInvalid", "Review and confirm the transfer details.");

        public static Error OwnershipTransferMissing =>
            Error.NotFound("Billing.OwnershipTransferMissing", "This ownership transfer is unavailable.");

        public static Error PlanVersionNotFound =>
            Error.NotFound("Billing.PlanVersionNotFound", "The plan version was not found.");

        public static Error SessionInvalid =>
            Error.Unauthorized("Billing.SessionInvalid", "A live verified merchant session is required.");

        public static Error ShopOwnerRequired =>
            Error.Forbidden("Billing.ShopOwnerRequired", "Select the shop you currently own.");
    }
}
