using ErrorOr;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using Errors = Intelibill.Application.Common.Errors.Errors;
using LicenceErrors = Intelibill.Application.Common.Errors.Errors.Licence;

namespace Intelibill.Api.Extensions;

public static class ErrorOrExtensions
{
    public static IActionResult ToActionResult<T>(this ErrorOr<T> errorOr, Func<T, IActionResult> onValue)
    {
        return errorOr.Match(
            value => onValue(value),
            errors => errors.ToProblemResult());
    }

    /// <summary>
    /// The 503 a switched-off or closed identity route answers with. Built directly rather than
    /// through <see cref="ControllerBase.Problem(string, string, int?, string, string, System.Collections.Generic.IDictionary{string, object})"/>
    /// so the shape is one decision in one place, and so a controller can be unit-tested without
    /// the whole MVC service graph behind it.
    /// </summary>
    public static IActionResult ToUnavailableResult(this Error error) =>
        new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = error.Code,
            Detail = error.Description,
        })
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable,
        };

    public static IActionResult ToProblemResult(this List<Error> errors)
    {
        if (errors.Count == 0)
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);

        var first = errors[0];

        var statusCode = first.Code == LicenceErrors.VerificationUnavailableCode
            ? StatusCodes.Status503ServiceUnavailable
            : first.Type switch
            {
                ErrorType.Validation => StatusCodes.Status400BadRequest,
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
                ErrorType.Forbidden => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status500InternalServerError
            };

        // Only these singleton, fixed-text problems can travel without shop authority.
        var problemDetails = errors.Count == 1 && IsDataFreeDenial(first)
            ? new DataFreeProblemDetails()
            : new ProblemDetails();
        problemDetails.Status = statusCode;
        problemDetails.Title = first.Code;
        problemDetails.Detail = first.Description;
        problemDetails.Extensions["errors"] = errors.Select(e => new { e.Code, e.Description });

        if (first.Code.StartsWith("Licence.", StringComparison.Ordinal))
            problemDetails.Extensions["code"] = first.Code;

        if (MemberCapacity(first) is { } capacity)
            problemDetails.Extensions["memberCapacity"] = capacity;

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

    private static bool IsDataFreeDenial(Error error) => error.Metadata is null &&
        (error == Errors.Shop.MembershipNotFound || error == Errors.Auth.SessionInvalidated || error == Errors.Expiry.OwnerRequired ||
         error == LicenceErrors.Busy || error == LicenceErrors.VerificationUnavailable ||
         error == LicenceErrors.SecuritySuspended);

    // Seat usage travels only when the application attached it for an entitled caller.
    private static object? MemberCapacity(Error error) =>
        error.Code == LicenceErrors.MemberCapacityReachedCode
        && error.Metadata?.GetValueOrDefault("memberAllowance") is int allowance
        && error.Metadata.GetValueOrDefault("activeMembers") is int active
            ? new { memberAllowance = allowance, activeMembers = active }
            : null;
}

internal sealed class DataFreeProblemDetails : ProblemDetails;
