using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

public static class CheckoutOrderErrors
{
    public static Error Invalid => Error.Validation(CheckoutOrdersV1.Invalid, "Choose a supported full-payment package and confirm its terms.");
    public static Error Missing => Error.NotFound(CheckoutOrdersV1.NotFound, "The quote was not found.");
    public static Error Changed => Error.Conflict(CheckoutOrdersV1.SourceChanged, "The quote revision or covered shops changed.");
    public static Error Expired => Error.Conflict(CheckoutOrdersV1.Expired, "The quote expired before acceptance.");
    public static Error Accepted => Error.Conflict(CheckoutOrdersV1.AlreadyAccepted, "The quote was already accepted.");
}
