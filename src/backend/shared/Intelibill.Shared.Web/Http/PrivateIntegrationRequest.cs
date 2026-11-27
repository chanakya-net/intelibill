using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http.Features;

namespace Intelibill.Shared.Web.Http;

public static class PrivateIntegrationRequest
{
    public static async Task<(string Token, byte[] Body)?> ReadAsync(HttpContext context, string path, int limit)
    {
        context.Response.Headers.CacheControl = "no-store";
        var request = context.Request;
        var rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget;
        if (request.Path.Value != path || request.PathBase.HasValue || request.QueryString.HasValue ||
            (!string.IsNullOrEmpty(rawTarget) && rawTarget != path) || !request.HasJsonContentType() ||
            request.Headers.Authorization.Count != 1 ||
            !AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var authorization) ||
            !authorization.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(authorization.Parameter) ||
            request.ContentLength > limit)
            return null;

        var buffer = new byte[limit + 1];
        var read = await request.Body.ReadAtLeastAsync(buffer, buffer.Length, false,
            context.RequestAborted);
        return read is > 0 && read <= limit ? (authorization.Parameter, buffer[..read]) : null;
    }
}
