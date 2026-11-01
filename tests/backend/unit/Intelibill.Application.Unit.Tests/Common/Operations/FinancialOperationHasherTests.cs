using Intelibill.Application.Common.Operations;

namespace Intelibill.Application.Unit.Tests.Common.Operations;

public class FinancialOperationHasherTests
{
    private sealed record Payload(Guid TargetId, decimal Amount, DateOnly Date, string? Notes);

    private static readonly Guid Target = Guid.Parse("8b8f6a52-3f0c-4a3e-9c3a-2f8a1d0b7e11");
    private static readonly DateOnly Date = new(2026, 11, 1);

    [Fact]
    public void Compute_SamePayload_IsStableSha256Hex()
    {
        var first = FinancialOperationHasher.Compute(new Payload(Target, 500m, Date, "cash"));
        var second = FinancialOperationHasher.Compute(new Payload(Target, 500m, Date, "cash"));

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void Compute_ChangedAmount_ChangesHash()
    {
        Assert.NotEqual(
            FinancialOperationHasher.Compute(new Payload(Target, 500m, Date, null)),
            FinancialOperationHasher.Compute(new Payload(Target, 501m, Date, null)));
    }

    [Fact]
    public void NormalizeAmount_TrailingZeros_HashAlike()
    {
        Assert.Equal(
            FinancialOperationHasher.Compute(new Payload(Target, FinancialOperationHasher.NormalizeAmount(500m), Date, null)),
            FinancialOperationHasher.Compute(new Payload(Target, FinancialOperationHasher.NormalizeAmount(500.00m), Date, null)));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  key-1 ", "key-1")]
    public void NormalizeKey_TrimsAndTreatsBlankAsAbsent(string? input, string? expected) =>
        Assert.Equal(expected, FinancialOperationHasher.NormalizeKey(input));
}
