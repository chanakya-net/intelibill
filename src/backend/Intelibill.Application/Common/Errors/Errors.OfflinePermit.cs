using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    /// <summary>Refusals of the signed offline licence permit. None of them changes stored evidence.</summary>
    public static class OfflinePermit
    {
        public static Error SigningUnavailable => Error.Failure("OfflinePermit.SigningUnavailable",
            "Offline permission cannot be signed right now.");

        public static Error AuthorityUnavailable => Error.Failure("OfflinePermit.AuthorityUnavailable",
            "Offline permission needs freshly verified trading access.");

        public static Error AuthorityChanged => Error.Conflict("OfflinePermit.AuthorityChanged",
            "The licence, membership or session behind this offline permission has changed. Reserve again online.");

        public static Error LeaseNotNew => Error.Conflict("OfflinePermit.LeaseNotNew",
            "Offline permission can only be anchored while its invoice lease is being reserved.");

        public static Error ScopeMismatch => Error.Forbidden("OfflinePermit.ScopeMismatch",
            "The offline permission does not belong to this shop, user, session, device or lease.");

        public static Error GrantNotFound => Error.NotFound("OfflinePermit.GrantNotFound",
            "No original verified grant is recorded for this lease.");

        public static Error SnapshotIncomplete => Error.Conflict("OfflinePermit.SnapshotIncomplete",
            "The stock snapshot is not a complete server capture for this lease.");

        public static Error TillRequired => Error.Conflict("OfflinePermit.TillRequired",
            "Offline permission needs your open till session for today.");

        public static Error Busy => Error.Conflict("OfflinePermit.Busy", "Offline permission is busy; try again.");

        public static Error DeadlineReached(string reason) => Error.Conflict("OfflinePermit.DeadlineReached",
            "The offline permission window has already ended.", new() { ["limitingReason"] = reason });

        public static Error Tightened => Error.Conflict("OfflinePermit.Tightened",
            "A stricter limit now applies to the recorded offline permission. Reserve again online.");

        public static Error Malformed => Error.Validation("OfflinePermit.Malformed", "The offline permission is malformed.");

        public static Error InvalidSignature => Error.Forbidden("OfflinePermit.InvalidSignature",
            "The offline permission signature is not valid.");

        public static Error UnknownKey => Error.Forbidden("OfflinePermit.UnknownKey",
            "The offline permission was signed by a key this shop has not retained.");

        public static Error KeyRebound => Error.Conflict("OfflinePermit.KeyRebound",
            "The signing key identifier is already retained with different public material.");

        public static Error OutsideValidity => Error.Forbidden("OfflinePermit.OutsideValidity",
            "The instant is outside the offline permission window.");
    }
}
