using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class RateLimit
    {
        public static Error Unavailable =>
            Error.Failure("RateLimit.Unavailable", "This service is temporarily unavailable. Try again shortly.");
    }
}
