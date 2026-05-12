using System.Net;
using System.Text.Json;

namespace Intelibill.Api.Middleware;

public partial class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            LogUnhandledException(logger, ex.Message, ex);
            await HandleExceptionAsync(context, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception: {Message}")]
    private static partial void LogUnhandledException(ILogger logger, string message, Exception ex);

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Safety net: ValidationMiddleware in the Wolverine pipeline should intercept all
        // validation failures before they escape as exceptions. If this branch fires in
        // production it indicates a registration gap — investigate ValidationMiddleware wiring.
        if (exception is FluentValidation.ValidationException ve)
        {
            var errors = ve.Errors.Select(e => new { code = e.PropertyName, description = e.ErrorMessage }).ToArray();

            var validationResponse = new
            {
                title = "One or more validation errors occurred.",
                status = 400,
                errors
            };

            context.Response.ContentType = "application/problem+json";
            context.Response.StatusCode = 400;
            return context.Response.WriteAsync(JsonSerializer.Serialize(validationResponse, JsonOptions));
        }

        var (statusCode, title) = exception switch
        {
            ArgumentException => (HttpStatusCode.BadRequest, "Bad Request"),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Unauthorized"),
            _ => (HttpStatusCode.InternalServerError, "Internal Server Error")
        };

        var response = new
        {
            type = $"https://httpstatuses.com/{(int)statusCode}",
            title,
            status = (int)statusCode,
            detail = exception.Message
        };

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)statusCode;

        return context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }
}
