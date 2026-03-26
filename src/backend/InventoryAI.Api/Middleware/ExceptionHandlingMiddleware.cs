using System.Net;
using System.Text.Json;
using FluentValidation;

namespace InventoryAI.Api.Middleware;

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
        if (exception is ValidationException validationEx)
        {
            var errors = validationEx.Errors
                .Select(e => new { code = e.PropertyName, description = e.ErrorMessage })
                .ToArray();

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
