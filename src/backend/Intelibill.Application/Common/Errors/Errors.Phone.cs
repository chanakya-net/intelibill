using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    /// <summary>
    /// Phone authentication errors. Every code is stable: clients branch on it, and the market
    /// readiness review names <c>Auth.PhoneEnrollmentUnavailable</c> as the response a closed
    /// enrollment route must give.
    /// </summary>
    public static class Phone
    {
        /// <summary>
        /// The legacy phone/name registration route. It granted a session on an unproved number,
        /// which left the registrant unable to sign in again once the session was gone and let
        /// anyone reserve someone else's number. It is closed; enrollment runs on proved
        /// challenges instead.
        /// </summary>
        public static Error EnrollmentUnavailable =>
            Error.Failure("Auth.PhoneEnrollmentUnavailable", "Phone sign-up is not available on this route. Verify a phone number, or create your account with an email address.");

        /// <summary>Phone authentication is switched off for this deployment.</summary>
        public static Error AuthenticationDisabled =>
            Error.Failure("Auth.PhoneEnrollmentUnavailable", "Phone sign-in is unavailable. Use your email address to sign in or recover your account.");

        public static Error InvalidPhoneNumber =>
            Error.Validation("Auth.InvalidPhoneNumber", "Enter a mobile number in international format, for example +919876543210.");

        public static Error InvalidChallenge =>
            Error.Validation("Auth.InvalidChallenge", "The code is incorrect, expired, superseded, or already used.");

        public static Error DeliveryUnavailable =>
            Error.Failure("Auth.PhoneDeliveryUnavailable", "Verification codes cannot be sent right now. Please try again later.");

        public static Error PhoneUnavailable =>
            Error.Conflict("Auth.PhoneUnavailable", "This number is unavailable. Request a new code or sign in to your existing account.");

        /// <summary>
        /// A number that already belongs to an account carrying business history. A carrier
        /// recycles numbers, so proving the number today is not proof of owning that account.
        /// </summary>
        public static Error RecoverySupportRequired =>
            Error.Conflict("Auth.RecoverySupportRequired", "This number belongs to an established account. Contact support for an ownership review; the account's data has been preserved.");

        public static Error ChangeRequiresProof =>
            Error.Conflict("Auth.PhoneChangeRequiresProof", "A verified phone number can only be changed by proving the new number. Use the phone change flow.");

        public static Error ReauthenticationRequired =>
            Error.Unauthorized("Auth.ReauthenticationRequired", "Please authenticate again.");
    }
}
