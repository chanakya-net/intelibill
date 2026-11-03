using Intelibill.Domain.Common;

namespace Intelibill.Domain.Unit.Tests.Common;

public class GstinTests
{
    [Theory]
    [InlineData("27AAPFU0939F1ZV")]
    [InlineData("29AAPFU0939F1ZR")]
    [InlineData(" 04aapfu0939f1z3 ")]
    public void Valid_gstins_pass(string value) => Assert.True(Gstin.IsValid(value));

    [Theory]
    [InlineData("27AAPFU0939F1ZW")] // check character changed
    [InlineData("27AAPFU0939F1Z")] // too short
    [InlineData("27AAPFU0939F1AV")] // 14th character must be Z
    [InlineData("96AAPFU0939F1ZV")] // not a domestic state code
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_gstins_fail(string? value) => Assert.False(Gstin.IsValid(value));

    [Fact]
    public void State_code_is_the_first_two_digits() => Assert.Equal("29", Gstin.StateCodeOf("29aapfu0939f1zr"));
}
