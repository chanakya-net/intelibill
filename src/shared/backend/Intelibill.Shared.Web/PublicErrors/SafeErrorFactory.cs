using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Shared.Web.PublicErrors;

/// <summary>A field-level reason a request was refused, safe to show the caller.</summary>
public sealed record PublicFieldError(string Code, string Description);

/// <summary>
/// What an exception is allowed to tell the caller: a stable code to translate, a status, and
/// the trace id that finds the matching diagnostic. Never the exception's text, type or stack.
/// </summary>
public sealed record PublicError(
    int Status,
    string Code,
    string Title,
    string TraceId,
    IReadOnlyList<PublicFieldError>? Errors = null)
{
    public string Type => $"https://httpstatuses.com/{Status}";
}

public interface ISafeErrorFactory
{
    PublicError Create(Exception exception, string traceId);
}

public sealed class SafeErrorFactory : ISafeErrorFactory
{
    public const string UnexpectedCode = "Internal.Unexpected";
    public const string ValidationCode = "Validation.Failed";
    public const string InvalidRequestCode = "Request.Invalid";
    public const string UnauthorizedCode = "Auth.Unauthorized";
    public const string ConcurrencyConflictCode = "Concurrency.Conflict";

    // Shorter attempted values are too likely to appear in an ordinary message by coincidence.
    private const int MinimumEchoLength = 4;
    private const string GenericFieldMessage = "The value is not valid.";

    public PublicError Create(Exception exception, string traceId)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            // Validation failures are authored by our validators and are the one place an
            // exception may carry caller-facing text.
            ValidationException validation => new PublicError(
                StatusCodes.Status400BadRequest,
                ValidationCode,
                "One or more validation errors occurred.",
                traceId,
                [.. validation.Errors.Select(ToFieldError)]),
            BadHttpRequestException badRequest => new PublicError(
                badRequest.StatusCode,
                InvalidRequestCode,
                "The request is invalid.",
                traceId),
            // A 400 does not make the text safe: an ArgumentException can be thrown from anywhere
            // and say anything, so the caller learns only that the request was refused.
            ArgumentException => new PublicError(
                StatusCodes.Status400BadRequest,
                InvalidRequestCode,
                "The request is invalid.",
                traceId),
            // A row-version check failed: another request changed the same record first. Nothing of
            // this request was saved, so the caller can reload and try again.
            DbUpdateConcurrencyException => new PublicError(
                StatusCodes.Status409Conflict,
                ConcurrencyConflictCode,
                "The record was changed by another request. Reload it and try again.",
                traceId),
            UnauthorizedAccessException => new PublicError(
                StatusCodes.Status401Unauthorized,
                UnauthorizedCode,
                "Unauthorized",
                traceId),
            _ => new PublicError(
                StatusCodes.Status500InternalServerError,
                UnexpectedCode,
                "An unexpected error occurred.",
                traceId),
        };
    }

    private static PublicFieldError ToFieldError(ValidationFailure failure) =>
        new(failure.PropertyName, EchoesAttemptedValue(failure) ? GenericFieldMessage : failure.ErrorMessage);

    // A message template may interpolate {PropertyValue}. A password, code or token the caller
    // typed must not come back in the response, where proxies and browser tooling keep it.
    private static bool EchoesAttemptedValue(ValidationFailure failure) =>
        failure.AttemptedValue is string { Length: >= MinimumEchoLength } attempted
        && failure.ErrorMessage.Contains(attempted, StringComparison.Ordinal);
}
