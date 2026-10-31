using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Unit.Tests.ValueObjects;

public class PhoneNumberFormatTests
{
    private const string India = "+91";

    [Theory]
    [InlineData("+919876543210", "+919876543210")]
    [InlineData("+91 98765 43210", "+919876543210")]
    [InlineData("+91-98765-43210", "+919876543210")]
    [InlineData("(+91) 98765 43210", "+919876543210")]
    [InlineData("+14155552671", "+14155552671")]
    [InlineData("9876543210", "+919876543210")]
    [InlineData("6012345678", "+916012345678")]
    public void TryNormalize_AcceptsUnambiguousInput(string input, string expected)
    {
        Assert.True(PhoneNumberFormat.TryNormalize(input, India, out var e164));
        Assert.Equal(expected, e164);
    }

    /// <summary>
    /// Each of these could be read more than one way, and a verification code sent to the wrong
    /// subscriber is not a recoverable mistake. The input is refused rather than rewritten.
    /// </summary>
    [Theory]
    [InlineData("0987654321")]          // trunk-prefixed: neither +910987654321 nor +919876543210
    [InlineData("1234567890")]          // ten digits, but not an Indian mobile range
    [InlineData("5876543210")]
    [InlineData("98765")]               // too short to be anything
    [InlineData("98765432100")]         // eleven digits
    [InlineData("+9198765432100000")]   // past E.164's fifteen digits
    [InlineData("+0119876543210")]      // a country code cannot start with zero
    [InlineData("+91987654321a")]
    [InlineData("+९१९८७६५४३२१०")]        // Devanagari digits parse as numbers but no carrier dials them
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryNormalize_RejectsAmbiguousInput_WithoutRewriting(string? input)
    {
        Assert.False(PhoneNumberFormat.TryNormalize(input, India, out var e164));
        Assert.Equal(string.Empty, e164);
    }

    /// <summary>
    /// Without a default country there is no national rule to apply, so a bare number stays
    /// refused. A country counts only once its rule is written in the value object.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("+1")]
    public void TryNormalize_BareNationalNumber_NeedsAStatedCountryRule(string? defaultCountryCode)
    {
        Assert.False(PhoneNumberFormat.TryNormalize("9876543210", defaultCountryCode, out _));
        Assert.True(PhoneNumberFormat.TryNormalize("+919876543210", defaultCountryCode, out var international));
        Assert.Equal("+919876543210", international);
    }

    [Fact]
    public void Mask_KeepsTheCountryCodeAndTheLastThreeDigits()
    {
        Assert.Equal("+91•••••••210", PhoneNumberFormat.Mask("+919876543210"));
    }

    [Fact]
    public void Mask_HidesAShortValueEntirely()
    {
        Assert.Equal("•••••", PhoneNumberFormat.Mask("+9198"));
        Assert.Equal(string.Empty, PhoneNumberFormat.Mask(string.Empty));
    }
}
