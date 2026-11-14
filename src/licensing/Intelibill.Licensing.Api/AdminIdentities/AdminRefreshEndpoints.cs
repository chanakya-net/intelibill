using Intelibill.Licensing.Api.Bootstrap;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Api.AdminIdentities;

internal static class AdminRefreshEndpoints
{
    internal static void MapAdminRefresh(this WebApplication app) =>
        app.MapPost("/api/admin/auth/refresh", RefreshAsync).AllowAnonymous();

    /// <summary>
    /// Origin and CSRF are checked before the credential is read, so a refused request never
    /// consumes it. Any refusal clears both session cookies; the browser must sign in again.
    /// </summary>
    private static async Task<IResult> RefreshAsync(HttpContext context, AdminSignInHttp http, AdminAccessTokens tokens,
        IAdminRefreshCredentials credentials)
    {
        var origin = await http.ReadOriginAsync(context);
        if (origin is null || !tokens.IsConfigured)
            return BootstrapHttp.Problem(context, 503, "Admin.SignInUnavailable", "Provider sign-in is unavailable.");
        if (!http.AcceptsMutation(context, origin)) return InvitationRequestGate.Refused(context);

        var result = await credentials.RotateAsync(AdminSignInHttp.ReadRefresh(context, origin), context.RequestAborted);
        if (result.IsError)
        {
            AdminSignInHttp.ClearSession(context, origin);
            return BootstrapHttp.Problem(context, 401, result.FirstError.Code, result.FirstError.Description);
        }

        var grant = result.Value;
        AdminSignInHttp.WriteSession(context, origin, tokens.Create(grant.Session, grant.AccessIssuedAt), grant);
        return Results.Ok(new { accessExpiresAt = grant.AccessIssuedAt + AdminSession.AccessLifetime });
    }
}
