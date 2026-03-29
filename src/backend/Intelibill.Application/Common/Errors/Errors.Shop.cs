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

        public static Error GstNumberInvalid =>
            Error.Validation("Shop.GstNumberInvalid", "GST number must be a valid Indian GSTIN.");

        public static Error UserNotFound =>
            Error.NotFound("Shop.UserNotFound", "The user was not found.");

        public static Error MembershipNotFound =>
            Error.Forbidden("Shop.MembershipNotFound", "You do not have access to this shop.");

        public static Error ShopNotFound =>
            Error.NotFound("Shop.ShopNotFound", "The shop was not found.");

        public static Error UserIsNotOwner =>
            Error.Forbidden("Shop.UserIsNotOwner", "Only the shop owner can update shop details.");

        public static Error UserIsNotOwnerForSwitch =>
            Error.Forbidden("Shop.UserIsNotOwnerForSwitch", "Only the shop owner can change the active shop.");

        public static Error ActiveShopNotSelected =>
            Error.Validation("Shop.ActiveShopNotSelected", "No active shop is selected for this session.");
    }
}