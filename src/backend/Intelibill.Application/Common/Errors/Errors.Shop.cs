using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Shop
    {
        public static Error NameRequired =>
            Error.Validation("Shop.NameRequired", "Shop name is required.");

        public static Error UserNotFound =>
            Error.NotFound("Shop.UserNotFound", "The user was not found.");

        public static Error MembershipNotFound =>
            Error.Forbidden("Shop.MembershipNotFound", "You do not have access to this shop.");
    }
}