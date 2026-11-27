using ErrorOr;
using Intelibill.Domain.Common;

namespace Intelibill.Domain.ValueObjects;

public static class QuantityPolicy
{
    /// <summary>Significant decimal places; trailing zeros are ignored.</summary>
    public static int ScaleOf(decimal value)
    {
        var scale = (int)((decimal.GetBits(value)[3] >> 16) & 0xFF);
        if (scale == 0)
            return 0;

        var normalized = value / 1.0000000000000000000000000000m;
        return (int)((decimal.GetBits(normalized)[3] >> 16) & 0xFF);
    }

    public static bool FitsScale(decimal value, int scale) => ScaleOf(value) <= scale;

    public static ErrorOr<decimal> Validate(decimal value, int scale, string unitLabel)
    {
        if (value <= 0m)
            return Errors.Quantity.NotPositive(unitLabel);

        if (!FitsScale(value, scale))
            return Errors.Quantity.PrecisionExceeded(value, unitLabel, scale);

        return value;
    }

    public static int ReturnScale(int? itemScale, decimal soldQuantity) =>
        Math.Max(itemScale ?? 0, ScaleOf(soldQuantity));
}
