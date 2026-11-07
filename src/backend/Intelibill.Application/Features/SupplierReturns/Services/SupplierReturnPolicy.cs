namespace Intelibill.Application.Features.SupplierReturns.Services;
public static class SupplierReturnPolicy
{
    public static decimal ReturnableQuantity(decimal available, decimal received, decimal shipped) => Math.Max(0, Math.Min(available,received-shipped));
}
