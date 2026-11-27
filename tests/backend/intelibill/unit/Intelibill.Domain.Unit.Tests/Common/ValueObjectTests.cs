using Intelibill.Domain.Common;

namespace Intelibill.Domain.Unit.Tests.Common;

public class ValueObjectTests
{
    private sealed class Money : ValueObject
    {
        public decimal Amount { get; }
        public string Currency { get; }

        public Money(decimal amount, string currency)
        {
            Amount = amount;
            Currency = currency;
        }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }
    }

    [Fact]
    public void Equals_WhenSameComponents_ReturnsTrue()
    {
        var a = new Money(100m, "INR");
        var b = new Money(100m, "INR");

        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Equals_WhenDifferentComponents_ReturnsFalse()
    {
        var a = new Money(100m, "INR");
        var b = new Money(200m, "INR");

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void EqualityOperator_WhenSameComponents_ReturnsTrue()
    {
        var a = new Money(50m, "USD");
        var b = new Money(50m, "USD");

        Assert.True(a == b);
    }

    [Fact]
    public void InequalityOperator_WhenDifferentComponents_ReturnsTrue()
    {
        var a = new Money(50m, "USD");
        var b = new Money(50m, "INR");

        Assert.True(a != b);
    }

    [Fact]
    public void GetHashCode_WhenSameComponents_ReturnsSameHash()
    {
        var a = new Money(100m, "INR");
        var b = new Money(100m, "INR");

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_WhenComparedToNull_ReturnsFalse()
    {
        var a = new Money(100m, "INR");
        Assert.False(a.Equals(null));
    }

    [Fact]
    public void Equals_ObjectOverload_WhenSameComponents_ReturnsTrue()
    {
        var a = new Money(100m, "INR");
        object b = new Money(100m, "INR");
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Equals_ObjectOverload_WhenNotValueObject_ReturnsFalse()
    {
        var a = new Money(100m, "INR");
        Assert.False(a.Equals("not a value object"));
    }

    [Fact]
    public void EqualityOperator_WhenBothNull_ReturnsTrue()
    {
        Money? a = null;
        Money? b = null;
        Assert.True(a == b);
    }

    [Fact]
    public void EqualityOperator_WhenLeftNull_ReturnsFalse()
    {
        Money? a = null;
        var b = new Money(100m, "INR");
        Assert.False(a == b);
    }
}
