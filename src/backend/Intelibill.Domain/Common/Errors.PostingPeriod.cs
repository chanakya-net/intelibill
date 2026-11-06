using ErrorOr;

namespace Intelibill.Domain.Common;

public static partial class Errors
{
    public static class PostingPeriod
    {
        public static Error AlreadyClosed =>
            Error.Conflict("PostingPeriod.AlreadyClosed", "This posting period is already closed.");

        public static Error AlreadyOpen =>
            Error.Conflict("PostingPeriod.AlreadyOpen", "This posting period is already open.");

        public static Error NotEnded =>
            Error.Conflict("PostingPeriod.NotEnded", "A posting period can be closed only after its last day has passed.");

        public static Error ReasonRequired =>
            Error.Validation("PostingPeriod.ReasonRequired", "A reason of up to 500 characters is required.");
    }
}
