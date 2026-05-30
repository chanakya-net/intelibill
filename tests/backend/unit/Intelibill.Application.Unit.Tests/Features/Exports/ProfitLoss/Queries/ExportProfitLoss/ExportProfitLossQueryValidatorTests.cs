using FluentValidation.TestHelper;
using Intelibill.Application.Features.Exports.ProfitLoss.Queries.ExportProfitLoss;

namespace Intelibill.Application.Unit.Tests.Features.Exports.ProfitLoss.Queries.ExportProfitLoss;

public sealed class ExportProfitLossQueryValidatorTests
{
    private readonly ExportProfitLossQueryValidator _validator = new();

    private static ExportProfitLossQuery ValidQuery() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            Type: "all",
            Search: "abc",
            Format: "xlsx");

    [Fact]
    public void Validate_WhenDateRangeIsInvalid_ReturnsError()
    {
        var query = ValidQuery() with
        {
            From = new DateOnly(2026, 5, 10),
            To = new DateOnly(2026, 5, 1),
        };

        var result = _validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x)
            .WithErrorMessage("'from' date must be before or equal to 'to' date.");
    }

    [Fact]
    public void Validate_WhenTypeIsUnsupported_ReturnsError()
    {
        var query = ValidQuery() with { Type = "bad-type" };

        var result = _validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.Type)
            .WithErrorMessage("'type' must be one of: all, sale, saleReturn, inventoryAdjustment.");
    }

    [Fact]
    public void Validate_WhenFormatIsUnsupported_ReturnsError()
    {
        var query = ValidQuery() with { Format = "pdf" };

        var result = _validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.Format)
            .WithErrorMessage("'format' must be xlsx.");
    }

    [Fact]
    public void Validate_WithValidInput_Passes()
    {
        var result = _validator.TestValidate(ValidQuery());

        result.ShouldNotHaveAnyValidationErrors();
    }
}
