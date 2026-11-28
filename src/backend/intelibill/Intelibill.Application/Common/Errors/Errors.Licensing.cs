using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Licensing
    {
        public static Error ShopScopeRequired =>
            Error.Forbidden("Licensing.ShopScopeRequired", "Select this shop as your active shop to read its access status.");
    }
}
