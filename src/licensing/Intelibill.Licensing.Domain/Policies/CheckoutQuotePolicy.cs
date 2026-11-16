namespace Intelibill.Licensing.Domain.Policies;

public static class CheckoutQuotePolicy
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public static (int Capacity, long Amount)? Price(int included, int extra, int named, long? basePrice, long? extraPrice)
    {
        if (included is < 1 or > CatalogueDraftRules.MaxCapacity || named < 1 ||
            extra != Math.Max(0, named - included) || extra > CatalogueDraftRules.MaxCapacity - included ||
            basePrice is not (>= 0 and <= CatalogueDraftRules.MaxPaise) ||
            (extra > 0 && extraPrice is not (>= 0 and <= CatalogueDraftRules.MaxPaise)))
            return null;

        // Both operands are bounded by the catalogue before multiplying.
        var total = checked(basePrice.Value + extra * (extraPrice ?? 0));
        return total is > 0 and <= CatalogueDraftRules.MaxPaise ? (included + extra, total) : null;
    }
}
