using System.Security.Cryptography;
using System.Text;
using HsnFinder.Api.Configuration;
using Microsoft.Extensions.Options;

namespace HsnFinder.Api.Middleware;

public sealed class ApiKeyMiddleware(
    IOptions<ApiKeySettings> apiKeySettings,
    ILogger<ApiKeyMiddleware> logger) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path;

        if (path.StartsWithSegments("/openapi") || path.StartsWithSegments("/scalar"))
        {
            await next(context);
            return;
        }

        var providedKey = context.Request.Headers["X-Api-Key"].ToString();

        if (string.IsNullOrWhiteSpace(providedKey))
        {
            logger.LogWarning("Request rejected — missing X-Api-Key header");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var expectedKey = apiKeySettings.Value.Key ?? string.Empty;
        var providedBytes = Encoding.UTF8.GetBytes(providedKey);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedKey);
        var isValid = providedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);

        if (!isValid)
        {
            logger.LogWarning("Request rejected — invalid X-Api-Key");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
}
