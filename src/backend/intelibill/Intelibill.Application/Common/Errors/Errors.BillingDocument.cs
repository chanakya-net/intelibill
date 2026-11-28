using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class BillingDocument
    {
        public static Error Invalid =>
            Error.Validation("BillingDocument.Invalid", "The version cursor must be greater than one.");
    }
}
