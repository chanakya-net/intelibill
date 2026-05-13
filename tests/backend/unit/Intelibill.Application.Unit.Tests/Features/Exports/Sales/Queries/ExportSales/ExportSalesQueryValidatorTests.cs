using FluentValidation.TestHelper;
using Intelibill.Application.Features.Exports.Sales.Queries.ExportSales;

namespace Intelibill.Application.Unit.Tests.Features.Exports.Sales.Queries.ExportSales;

public class ExportSalesQueryValidatorTests
{
    private readonly ExportSalesQueryValidator _validator = new();

    private static ExportSalesQuery ValidQuery() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "xlsx",
            "summary",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow));

    [Fact]
    public void Validate_WhenStartDateGreaterThanEndDate_ReturnsError()
    {
        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        var query = ValidQuery() with { StartDate = now.AddDays(10), EndDate = now };

        var result = _validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.StartDate)
            .WithErrorMessage("Start date must be on or before end date.");
    }

    [Fact]
    public void Validate_WhenDateRangeExceeds366Days_ReturnsError()
    {
        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        var query = ValidQuery() with { StartDate = now.AddDays(-400), EndDate = now };

        var result = _validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.StartDate)
            .WithErrorMessage("Date range must not exceed 366 days.");
    }

    [Fact]
    public void Validate_WithValidDateRange_PassesValidation()
    {
        var query = ValidQuery();

        var result = _validator.TestValidate(query);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WhenFormat_Invalid_ReturnsError()
    {
        var query = ValidQuery() with { Format = "invalid" };

        var result = _validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.Format);
    }

    [Fact]
    public void Validate_WhenLevel_Invalid_ReturnsError()
    {
        var query = ValidQuery() with { Level = "invalid" };

        var result = _validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.Level);
    }
}
