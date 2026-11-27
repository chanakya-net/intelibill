namespace Intelibill.Domain.ValueObjects;

public enum InstantDiscountType
{
    None = 0,
    Percentage = 1,
    Flat = 2,
}

public sealed record InstantDiscount(
    InstantDiscountType Type,
    decimal Value);

