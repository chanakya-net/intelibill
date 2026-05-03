using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Supplier
    {
        public static Error NameRequired =>
            Error.Validation("Supplier.NameRequired", "Supplier name is required.");

        public static Error AddressRequired =>
            Error.Validation("Supplier.AddressRequired", "Supplier address is required.");

        public static Error CityRequired =>
            Error.Validation("Supplier.CityRequired", "Supplier city is required.");

        public static Error StateRequired =>
            Error.Validation("Supplier.StateRequired", "Supplier state is required.");

        public static Error PinRequired =>
            Error.Validation("Supplier.PinRequired", "Supplier pin is required.");

        public static Error ContactPersonPhoneInvalid =>
            Error.Validation("Supplier.ContactPersonPhoneInvalid", "Contact person phone must contain 7 to 15 digits and may include leading +.");

        public static Error SupplierNotFound =>
            Error.NotFound("Supplier.SupplierNotFound", "Supplier was not found.");

        public static Error UserIsNotOwner =>
            Error.Forbidden("Supplier.UserIsNotOwner", "Only the shop owner can manage suppliers.");

        public static Error UserIsNotOwnerOrManager =>
            Error.Forbidden("Supplier.UserIsNotOwnerOrManager", "Only owner or manager can record payments.");

        public static Error PaymentAmountMustBePositive =>
            Error.Validation("Supplier.PaymentAmountMustBePositive", "Payment amount must be greater than zero.");

        public static Error ShopOwnerNotFound =>
            Error.Unexpected("Supplier.ShopOwnerNotFound", "Unable to resolve the shop owner for supplier listing.");

        public static Error CannotModifySystemSupplier =>
            Error.Conflict("Supplier.CannotModifySystemSupplier", "System supplier cannot be modified.");

        public static Error CannotReassignFromRealSupplier =>
            Error.Conflict("Supplier.CannotReassignFromRealSupplier", "Reassignment is only allowed when current supplier is system-generated.");

        public static Error SystemSupplierNotFound =>
            Error.Unexpected("Supplier.SystemSupplierNotFound", "System supplier not found for user. User registration may be incomplete.");
    }
}
