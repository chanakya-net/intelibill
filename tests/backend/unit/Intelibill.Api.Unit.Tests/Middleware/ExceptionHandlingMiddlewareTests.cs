using System.Text;
using FluentValidation;
using FluentValidation.Results;
using Intelibill.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Intelibill.Api.Unit.Tests.Middleware;

public class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenValidationException_ReturnsProblemJsonWithBadRequest()
    {
        var exception = new ValidationException([
            new ValidationFailure("Email", "Email is required")
        ]);

        var (context, payload) = await InvokeAndReadAsync(exception);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Contains("validation errors", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("email", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Email is required", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_WhenArgumentException_ReturnsBadRequest()
    {
        var (context, payload) = await InvokeAndReadAsync(new ArgumentException("Invalid argument"));

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Contains("Bad Request", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Invalid argument", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_WhenUnauthorizedAccessException_ReturnsUnauthorized()
    {
        var (context, payload) = await InvokeAndReadAsync(new UnauthorizedAccessException("Unauthorized"));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Contains("Unauthorized", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_WhenUnhandledException_ReturnsInternalServerError()
    {
        var (context, payload) = await InvokeAndReadAsync(new InvalidOperationException("Unexpected"));

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Contains("Internal Server Error", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Unexpected", payload, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(HttpContext Context, string Payload)> InvokeAndReadAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        RequestDelegate next = _ => throw exception;
        var middleware = new ExceptionHandlingMiddleware(next, NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        var payload = await reader.ReadToEndAsync();

        return (context, payload);
    }
}
