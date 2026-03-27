using ErrorOr;
using Intelibill.Api.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Unit.Tests.Extensions;

public class ErrorOrExtensionsTests
{
    [Fact]
    public void ToActionResult_WhenValue_ReturnsMappedValueResult()
    {
        ErrorOr<int> value = 42;

        var result = value.ToActionResult(v => new OkObjectResult(v));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(42, ok.Value);
    }

    [Theory]
    [InlineData(ErrorType.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorType.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(ErrorType.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(ErrorType.Unexpected, StatusCodes.Status500InternalServerError)]
    public void ToProblemResult_MapsErrorTypeToStatusCode(ErrorType errorType, int expectedStatusCode)
    {
        var error = errorType switch
        {
            ErrorType.Validation => Error.Validation("Test.Code", "Test description"),
            ErrorType.NotFound => Error.NotFound("Test.Code", "Test description"),
            ErrorType.Conflict => Error.Conflict("Test.Code", "Test description"),
            ErrorType.Unauthorized => Error.Unauthorized("Test.Code", "Test description"),
            ErrorType.Forbidden => Error.Forbidden("Test.Code", "Test description"),
            _ => Error.Unexpected("Test.Code", "Test description")
        };

        var errors = new List<Error> { error };

        var result = errors.ToProblemResult();

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatusCode, objectResult.StatusCode);

        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(expectedStatusCode, problem.Status);
        Assert.Equal("Test.Code", problem.Title);
        Assert.Equal("Test description", problem.Detail);
        Assert.True(problem.Extensions.ContainsKey("errors"));
    }

    [Fact]
    public void ToProblemResult_WhenNoErrors_ReturnsInternalServerErrorStatusCodeResult()
    {
        var result = new List<Error>().ToProblemResult();

        var statusCodeResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusCodeResult.StatusCode);
    }
}
