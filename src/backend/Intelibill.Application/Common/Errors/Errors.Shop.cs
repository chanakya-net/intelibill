using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Shop
    {
        public static Error NameRequired =>
            Error.Validation("Shop.NameRequired", "Shop name is required.");

        public static Error AddressRequired =>
            Error.Validation("Shop.AddressRequired", "Shop address is required.");

        public static Error CityRequired =>
            Error.Validation("Shop.CityRequired", "Shop city is required.");

        public static Error StateRequired =>
            Error.Validation("Shop.StateRequired", "Shop state is required.");

        public static Error PincodeRequired =>
            Error.Validation("Shop.PincodeRequired", "Shop pincode is required.");

        public static Error UserNotFound =>
            Error.NotFound("Shop.UserNotFound", "The user was not found.");

        public static Error MembershipNotFound =>
            Error.Forbidden("Shop.MembershipNotFound", "You do not have access to this shop.");
    }
}