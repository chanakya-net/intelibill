using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class InvoiceLease
    {
        public static Error DeviceIdRequired =>
            Error.Validation("InvoiceLease.DeviceIdRequired", "Device id is required.");

        public static Error BlockSizeInvalid =>
            Error.Validation("InvoiceLease.BlockSizeInvalid", "Block size must be greater than zero.");

        public static Error BlockSizeTooLarge =>
            Error.Validation("InvoiceLease.BlockSizeTooLarge", "Block size must be 1000 or less.");

        public static Error UserNotAuthorized =>
            Error.Forbidden("InvoiceLease.UserNotAuthorized", "Only owners or managers can reserve invoice numbers.");
    }
}
