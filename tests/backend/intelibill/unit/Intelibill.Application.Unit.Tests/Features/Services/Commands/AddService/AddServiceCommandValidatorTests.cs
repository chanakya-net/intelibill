using FluentValidation.TestHelper;
using Intelibill.Application.Features.Services.Commands.AddService;

namespace Intelibill.Application.Unit.Tests.Features.Services.Commands.AddService;

public class AddServiceCommandValidatorTests
{
    private readonly AddServiceCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenNameIsEmpty_ReturnsError()
    {
        var command = new AddServiceCommand(
            new(Guid.NewGuid(), Guid.NewGuid(), 0, 0, Guid.NewGuid(), "Owner"),
            Name: string.Empty,
            Description: null,
            Price: 10m,
            HsnCode: null,
            TaxRatePercent: 5m,
            TaxIncluded: false,
            IsActive: true);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_WhenPriceIsZero_ReturnsError()
    {
        var command = new AddServiceCommand(
            new(Guid.NewGuid(), Guid.NewGuid(), 0, 0, Guid.NewGuid(), "Owner"),
            Name: "Service",
            Description: null,
            Price: 0m,
            HsnCode: null,
            TaxRatePercent: 5m,
            TaxIncluded: false,
            IsActive: true);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Price);
    }

    [Theory]
    [InlineData("ABC")]
    [InlineData("123")]
    public void Validate_WhenHsnCodeInvalid_ReturnsError(string hsnCode)
    {
        var command = CreateValidCommand() with { HsnCode = hsnCode };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.HsnCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Validate_WhenTaxRateInvalid_ReturnsError(decimal taxRate)
    {
        var command = CreateValidCommand() with { TaxRatePercent = taxRate };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.TaxRatePercent);
    }

    private static AddServiceCommand CreateValidCommand() =>
        new(
            new(Guid.NewGuid(), Guid.NewGuid(), 0, 0, Guid.NewGuid(), "Owner"),
            Name: "Consulting",
            Description: "Professional support",
            Price: 120m,
            HsnCode: "1001",
            TaxRatePercent: 18m,
            TaxIncluded: false,
            IsActive: true);
}
