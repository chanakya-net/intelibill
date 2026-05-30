using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Service
    {
        public static Error NameRequired =>
            Error.Validation("Service.NameRequired", "Service name is required.");

        public static Error DescriptionTooLong =>
            Error.Validation("Service.DescriptionTooLong", "Description exceeds allowed length.");

        public static Error PriceInvalid =>
            Error.Validation("Service.PriceInvalid", "Service price must be greater than zero.");

        public static Error TaxRateInvalid =>
            Error.Validation("Service.TaxRateInvalid", "Tax rate must be between 0 and 100.");

        public static Error HsnCodeInvalid =>
            Error.Validation("Service.HsnCodeInvalid", "HSN code must be 4 to 8 digits.");

        public static Error NameAlreadyExists =>
            Error.Conflict("Service.NameAlreadyExists", "A service with this name already exists in the active shop.");

        public static Error CodeAlreadyExists =>
            Error.Conflict("Service.CodeAlreadyExists", "A service with this code already exists in the active shop.");

        public static Error NotFound =>
            Error.NotFound("Service.NotFound", "Service not found.");

        public static Error UserIsNotOwnerOrManager =>
            Error.Forbidden("Service.UserIsNotOwnerOrManager", "Only owner or manager can manage services.");
    }
}
