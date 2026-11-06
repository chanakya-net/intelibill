using System.Globalization;
using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class PostingPeriod
    {
        public static Error OwnerRequired =>
            Error.Forbidden("PostingPeriod.OwnerRequired", "Only an owner can close or reopen a posting period.");

        public static Error OwnerOrManagerRequired =>
            Error.Forbidden("PostingPeriod.OwnerOrManagerRequired", "Only an owner or manager can view posting periods.");

        public static Error Closed(DateOnly periodStart) =>
            Error.Conflict(
                "PostingPeriod.Closed",
                $"{periodStart.ToString("MMMM yyyy", CultureInfo.InvariantCulture)} is closed. Ask an owner to reopen it before posting into it.");

        public static Error Busy =>
            Error.Conflict("PostingPeriod.Busy", "The posting period is being closed or reopened. Try again in a moment.");

        public static Error PeriodStartInvalid =>
            Error.Validation("PostingPeriod.PeriodStartInvalid", "The period must start on the first day of a month.");

        public static Error PeriodInvalid =>
            Error.Validation("PostingPeriod.PeriodInvalid", "The period must be a month written as yyyy-MM.");

        public static Error FiscalYearInvalid =>
            Error.Validation("PostingPeriod.FiscalYearInvalid", "The fiscal year must be a four digit start year.");

        public static Error OperationKeyRequired =>
            Error.Validation("PostingPeriod.OperationKeyRequired", "An operation key is required.");

        public static Error ReasonRequired =>
            Error.Validation("PostingPeriod.ReasonRequired", "A reason of up to 500 characters is required.");
    }
}
