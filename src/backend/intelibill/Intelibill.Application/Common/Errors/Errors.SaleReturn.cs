using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class SaleReturn
    {
        public static Error SaleCancelled =>
            Error.Conflict("SaleReturn.SaleCancelled", "A cancelled sale cannot be returned.");
    }
}
