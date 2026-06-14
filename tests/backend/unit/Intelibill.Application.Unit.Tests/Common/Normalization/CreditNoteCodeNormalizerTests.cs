using Intelibill.Application.Common.Normalization;

namespace Intelibill.Application.Unit.Tests.Common.Normalization;

public sealed class CreditNoteCodeNormalizerTests
{
    [Theory]
    [InlineData("   ", "")]
    [InlineData("---", "")]
    [InlineData("  cN - 001  ", "CN001")]
    [InlineData("CN-2026 0601-ABC", "CN20260601ABC")]
    public void Normalize_RemovesWhitespaceSeparatorsAndUppercases(string input, string expected)
    {
        var normalized = CreditNoteCodeNormalizer.Normalize(input);

        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void Normalize_AlreadyNormalizedIsIdempotent()
    {
        const string input = "CN-20260425-ABC123";
        var normalized = CreditNoteCodeNormalizer.Normalize(input);
        var normalizedAgain = CreditNoteCodeNormalizer.Normalize(normalized);

        Assert.Equal(normalized, normalizedAgain);
    }

    [Fact]
    public void Normalize_EmptyStringReturnsEmpty()
    {
        Assert.Equal(string.Empty, CreditNoteCodeNormalizer.Normalize(string.Empty));
    }
}
