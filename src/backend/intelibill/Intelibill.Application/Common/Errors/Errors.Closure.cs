using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Closure
    {
        public static Error AccountClosed =>
            Error.Conflict("Closure.AccountClosed", "Operational closure is already saved for this billing account.");

        public static Error AffiliationUnavailable =>
            Error.Conflict("Closure.AffiliationUnavailable", "The shop affiliation needs review before closure.");

        public static Error MerchantUnavailable =>
            Error.Failure("Closure.MerchantUnavailable", "The merchant service could not validate closure.");

        public static Error ProposalUnavailable =>
            Error.NotFound("Closure.ProposalUnavailable", "No current closure proposal is available.");

        public static Error ReviewChanged =>
            Error.Conflict("Closure.ReviewChanged", "The account closure proposal changed.");

        public static Error ScopeTooLarge =>
            Error.Conflict("Closure.ScopeTooLarge", "The complete account scope exceeds the closure review limit.");
    }
}
