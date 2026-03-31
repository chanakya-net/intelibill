using Intelibill.Application.Features.Shops.Commands.CreateShop;

namespace Intelibill.Application.Unit.Tests.Features.Shops.Commands.CreateShop;

public class CreateShopCommandValidatorTests
{
    private readonly CreateShopCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_PassesValidation()
    {
        var command = CreateValidCommand();

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_NameEmpty_FailsValidation(string? name)
    {
        var command = CreateValidCommand() with { Name = name! };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_NameTooLong_FailsValidation()
    {
        var command = CreateValidCommand() with { Name = new string('a', 121) };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_AddressEmpty_FailsValidation(string? address)
    {
        var command = CreateValidCommand() with { Address = address! };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Address");
    }

    [Fact]
    public void Validate_AddressTooLong_FailsValidation()
    {
        var command = CreateValidCommand() with { Address = new string('a', 321) };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Address");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_CityEmpty_FailsValidation(string? city)
    {
        var command = CreateValidCommand() with { City = city! };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "City");
    }

    [Fact]
    public void Validate_CityTooLong_FailsValidation()
    {
        var command = CreateValidCommand() with { City = new string('a', 121) };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "City");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_StateEmpty_FailsValidation(string? state)
    {
        var command = CreateValidCommand() with { State = state! };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "State");
    }

    [Fact]
    public void Validate_StateTooLong_FailsValidation()
    {
        var command = CreateValidCommand() with { State = new string('a', 121) };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "State");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_PincodeEmpty_FailsValidation(string? pincode)
    {
        var command = CreateValidCommand() with { Pincode = pincode! };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Pincode");
    }

    [Fact]
    public void Validate_PincodeTooLong_FailsValidation()
    {
        var command = CreateValidCommand() with { Pincode = new string('1', 17) };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Pincode");
    }

    [Fact]
    public void Validate_ContactPersonTooLong_FailsValidation()
    {
        var command = CreateValidCommand() with { ContactPerson = new string('a', 121) };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "ContactPerson");
    }

    [Fact]
    public void Validate_MobileNumberTooLong_FailsValidation()
    {
        var command = CreateValidCommand() with { MobileNumber = new string('1', 33) };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "MobileNumber");
    }

    [Fact]
    public void Validate_GstNumberTooLong_FailsValidation()
    {
        var command = CreateValidCommand() with { GstNumber = new string('1', 21) };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "GstNumber");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_GstNumberEmpty_PassesValidation(string? gstNumber)
    {
        var command = CreateValidCommand() with { GstNumber = gstNumber };

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("INVALID")]
    [InlineData("27AAPFU0939F1ZVextra")]
    [InlineData("00AAAAA0000A0Z0")]
    [InlineData("ABCD1234567A1Z0")]
    public void Validate_GstNumberInvalid_FailsValidation(string gstNumber)
    {
        var command = CreateValidCommand() with { GstNumber = gstNumber };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "GstNumber");
    }

    [Theory]
    [InlineData("27AAPFU0939F1ZV")]
    [InlineData("29AAPFU0939F1ZV")]
    [InlineData("27BBBBB1111A1Z9")]
    [InlineData("27CCCCC2222C1Z8")]
    public void Validate_GstNumberValid_PassesValidation(string gstNumber)
    {
        var command = CreateValidCommand() with { GstNumber = gstNumber };

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_UserIdEmpty_FailsValidation()
    {
        var command = CreateValidCommand() with { UserId = Guid.Empty };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "UserId");
    }

    [Fact]
    public void Validate_ValidCommandWithOptionalFields_PassesValidation()
    {
        var command = new CreateShopCommand(
            Guid.NewGuid(),
            "Test Shop",
            "123 Test Street",
            "Test City",
            "Test State",
            "123456",
            "Contact Person",
            "9876543210",
            "27AAPFU0939F1ZV");

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    private static CreateShopCommand CreateValidCommand() =>
        new(
            Guid.NewGuid(),
            "Valid Shop Name",
            "123 Valid Address",
            "Valid City",
            "Valid State",
            "123456",
            null,
            null,
            null);
}