using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Hsn
    {
        public static readonly Error EmptyProductName =
            Error.Validation("hsn.empty_product_name", "Product name is required.");

        public static readonly Error LookupFailed =
            Error.Failure("hsn.lookup_failed", "HSN lookup failed.");

        public static readonly Error InvalidExternalResponse =
            Error.Failure("hsn.invalid_external_response", "HSN service returned an invalid response.");
    }
}
