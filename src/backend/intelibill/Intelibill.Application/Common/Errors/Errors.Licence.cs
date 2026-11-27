using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Licence
    {
        public const string BusyCode = "Licence.Busy";

        public static Error Busy => Error.Conflict(BusyCode, "The shop licence is busy; try again.");

        public static Error ScopeMismatch =>
            Error.Forbidden("Licence.ScopeMismatch", "The transaction is not scoped to this shop.");

        public static Error PendingActivation =>
            Error.Forbidden("Licence.PendingActivation", "The shop does not have active trading access.");

        public static Error Expired =>
            Error.Forbidden("Licence.Expired", "The shop's trading access has ended.");

        public static Error TradingRestricted =>
            Error.Forbidden("Licence.TradingRestricted", "Trading is restricted for this shop.");

        public static Error SecuritySuspended =>
            Error.Forbidden("Licence.SecuritySuspended", "Access to this shop is suspended.");

        public const string MemberCapacityReachedCode = "Licence.MemberCapacityReached";

        /// <summary>
        /// Usage figures are attached only for a caller entitled to see them (the shop owner);
        /// an invitee learns that the shop is full and nothing more.
        /// </summary>
        public static Error MemberCapacityReached(int? memberAllowance = null, int? activeMembers = null) =>
            Error.Conflict(MemberCapacityReachedCode,
                "The shop has no free member seat. Suspend or remove a member, or increase the shop's member allowance.",
                memberAllowance is { } allowance && activeMembers is { } active
                    ? new Dictionary<string, object> { ["memberAllowance"] = allowance, ["activeMembers"] = active }
                    : null);

        public const string VerificationUnavailableCode = "Licence.VerificationUnavailable";

        public static Error VerificationUnavailable => Error.Failure(VerificationUnavailableCode,
            "Trading access could not be verified. Try again shortly or contact support if the problem continues.");
    }
}
