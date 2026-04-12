using ErrorOr;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace Intelibill.Api.Extensions;

public static class ErrorOrExtensions
{
    public static IActionResult ToActionResult<T>(this ErrorOr<T> errorOr, Func<T, IActionResult> onValue)
    {
        return errorOr.Match(
            value => onValue(value),
            errors => errors.ToProblemResult());
    }

    public static IActionResult ToProblemResult(this List<Error> errors)
    {
        if (errors.Count == 0)
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);

        var first = errors[0];

        var statusCode = first.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = first.Code,
            Detail = first.Description,
            Extensions = { ["errors"] = errors.Select(e => new { e.Code, e.Description }) }
        };

        if (first.Type == ErrorType.Validation)
        {
            Log.Warning(
                "Validation failed. Codes={Codes} Descriptions={Descriptions}",
                errors.Select(static e => e.Code).ToArray(),
                errors.Select(static e => e.Description).ToArray());
        }
        else if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            Log.Error(
                "Request failed with server error result. Type={ErrorType} Codes={Codes} Descriptions={Descriptions}",
                first.Type,
                errors.Select(static e => e.Code).ToArray(),
                errors.Select(static e => e.Description).ToArray());
        }
        else
        {
            Log.Warning(
                "Request failed with client error result. Type={ErrorType} Codes={Codes} Descriptions={Descriptions}",
                first.Type,
                errors.Select(static e => e.Code).ToArray(),
                errors.Select(static e => e.Description).ToArray());
        }

        return new ObjectResult(problemDetails) { StatusCode = statusCode };
    }
}
