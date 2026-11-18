using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

public static class CheckoutPaymentErrors
{
    public static Error Invalid => Error.Validation(CheckoutOrdersV1.Invalid, "The payment request is invalid.");
    public static Error Unavailable => Error.Conflict(CheckoutPaymentsV1.Unavailable, "Online payment is not available here.");
    public static Error Unsupported => Error.Conflict(CheckoutPaymentsV1.Unsupported, "This order cannot be paid online yet.");
    public static Error NotAccepted => Error.Conflict(CheckoutPaymentsV1.NotAccepted, "Accept the quote before paying.");
    public static Error AlreadyStarted => Error.Conflict(CheckoutPaymentsV1.AlreadyStarted, "Payment for this order already started.");
    public static Error Missing => Error.NotFound(CheckoutPaymentsV1.NotFound, "The payment attempt was not found.");
    public static Error Busy => Error.Conflict(CheckoutPaymentsV1.Unavailable, "Payment storage is busy; try again.");
}
