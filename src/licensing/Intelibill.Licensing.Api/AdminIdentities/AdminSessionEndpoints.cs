using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Api.Bootstrap;
using Intelibill.Licensing.Application.AdminIdentities;
using Microsoft.AspNetCore.Http.Features;

namespace Intelibill.Licensing.Api.AdminIdentities;

internal static class AdminSessionEndpoints
{
    private const int MaxRevokeBytes = 1024;

    internal static void MapAdminSessions(this WebApplication app)
    {
        var sessions = app.MapGroup("/api/admin/sessions").RequireAuthorization(ProviderSessions.DevicePolicy);
        sessions.MapGet("", ListAsync);
        sessions.MapPost("/{sessionId:guid}/revoke", RevokeAsync);
        app.MapPost("/api/admin/auth/sign-out", SignOutAsync).AllowAnonymous();
    }

    private static async Task<IResult> ListAsync(HttpContext context, AdminDeviceSessions sessions)
    {
        if (ProviderSessions.ReadCaller(context.User) is not { } caller) return Problem(context, AdminRefreshErrors.SessionEnded);
        var query = context.Request.Query;
        if (query.Any(pair => (pair.Key != "pageSize" && pair.Key != "cursor") || pair.Value.Count != 1))
            return Problem(context, AdminDeviceSessions.InvalidQuery);
        var result = await sessions.ListAsync(caller, query["pageSize"].FirstOrDefault(),
            query["cursor"].FirstOrDefault(), context.RequestAborted);
        return result.IsError ? Problem(context, result.FirstError) : Results.Ok(result.Value);
    }

    private static async Task<IResult> RevokeAsync(Guid sessionId, HttpContext context, AdminSignInHttp http,
        AdminDeviceSessions sessions)
    {
        var origin = await http.ReadOriginAsync(context);
        if (origin is null || !http.AcceptsMutation(context, origin)) return InvitationRequestGate.Refused(context);
        if (ProviderSessions.ReadCaller(context.User) is not { } caller) return Problem(context, AdminRefreshErrors.SessionEnded);
        if (context.Request.ContentLength > MaxRevokeBytes)
            return BootstrapHttp.Problem(context, 413, "Admin.RequestTooLarge", "The request is too large.");
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
            size.MaxRequestBodySize = MaxRevokeBytes;
        var request = await ReadReasonAsync(context);
        var result = await sessions.RevokeAsync(caller, sessionId, request?.Reason, context.RequestAborted);
        if (result.IsError) return Problem(context, result.FirstError);
        if (caller.SessionId == sessionId) AdminSignInHttp.ClearSession(context, origin);
        return Results.NoContent();
    }

    private static async Task<RevokeRequest?> ReadReasonAsync(HttpContext context)
    {
        if (!context.Request.HasJsonContentType()) return null;
        try
        {
            return await context.Request.ReadFromJsonAsync<RevokeRequest>(context.RequestAborted);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<IResult> SignOutAsync(HttpContext context, AdminSignInHttp http,
        AdminAccessTokens tokens, AdminSignOutHandler handler)
    {
        var origin = await http.ReadOriginAsync(context);
        if (origin is null || !tokens.IsConfigured)
            return BootstrapHttp.Problem(context, 503, "Admin.SignInUnavailable", "Provider sign-in is unavailable.");
        if (!http.AcceptsMutation(context, origin)) return InvitationRequestGate.Refused(context);
        var access = tokens.Read(AdminSignInHttp.ReadAccess(context, origin));
        await handler.HandleAsync(AdminSignInHttp.ReadRefresh(context, origin),
            access is null ? null : new(access.AdminId, access.SessionId, access.ExpiresAt), context.RequestAborted);
        AdminSignInHttp.ClearSession(context, origin);
        return Results.NoContent();
    }

    private static IResult Problem(HttpContext context, Error error) => BootstrapHttp.Problem(context, error.Type switch
    {
        ErrorType.Unauthorized => 401,
        ErrorType.Forbidden => 403,
        ErrorType.NotFound => 404,
        _ => 400,
    }, error.Code, error.Description);

    private sealed record RevokeRequest(string? Reason);
}
