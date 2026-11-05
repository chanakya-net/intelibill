using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Unit.Tests.ValueObjects;

public class QuantityPolicyTests
{
    [Theory]
    [InlineData("0.25")]
    [InlineData("1.5")]
    [InlineData("0.001")]
    public void weighted_quantity_preserves_three_places(string text)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var result = QuantityPolicy.Validate(value, 3, "kg");
        Assert.False(result.IsError);
        Assert.Equal(value, result.Value);

        var tooFine = QuantityPolicy.Validate(0.0001m, 3, "kg");
        Assert.True(tooFine.IsError);
        Assert.Equal("Quantity.PrecisionExceeded", tooFine.FirstError.Code);
    }

    [Fact]
    public void pieces_reject_fraction()
    {
        var result = QuantityPolicy.Validate(0.5m, 0, "pcs");
        Assert.True(result.IsError);
        Assert.Equal("Quantity.PrecisionExceeded", result.FirstError.Code);
    }

    [Fact]
    public void trailing_zeros_are_not_precision()
    {
        Assert.False(QuantityPolicy.Validate(2.000m, 0, "pcs").IsError);
        Assert.Equal(2, QuantityPolicy.ScaleOf(0.250m));
        Assert.Equal(0, QuantityPolicy.ScaleOf(2.000m));
        Assert.True(QuantityPolicy.FitsScale(2.000m, 0));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void zero_and_negative_are_not_positive(string text)
    {
        var result = QuantityPolicy.Validate(decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture), 3, "kg");
        Assert.True(result.IsError);
        Assert.Equal("Quantity.NotPositive", result.FirstError.Code);
    }

    [Fact]
    public void return_scale_covers_legacy_fraction()
    {
        Assert.Equal(1, QuantityPolicy.ReturnScale(0, 0.5m));
        Assert.Equal(0, QuantityPolicy.ReturnScale(null, 2m));
    }
}
