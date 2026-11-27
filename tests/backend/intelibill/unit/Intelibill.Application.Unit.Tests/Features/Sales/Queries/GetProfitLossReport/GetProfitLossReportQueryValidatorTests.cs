using Intelibill.Application.Features.Sales.Queries.GetProfitLossReport;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Queries.GetProfitLossReport;

public class GetProfitLossReportQueryValidatorTests
{
    [Fact]
    public void Validate_WhenFromIsAfterTo_ReturnsError()
    {
        var validator = new GetProfitLossReportQueryValidator();
        var result = validator.Validate(new GetProfitLossReportQuery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            From: new DateOnly(2026, 5, 5),
            To: new DateOnly(2026, 5, 1)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorCode == "Sales.ProfitLoss.InvalidDateRange");
    }

    [Fact]
    public void Validate_WhenTypeIsUnsupported_ReturnsError()
    {
        var validator = new GetProfitLossReportQueryValidator();
        var result = validator.Validate(new GetProfitLossReportQuery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Type: "bad-type"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorCode == "Sales.ProfitLoss.InvalidType");
    }
}
