using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Api.Catalogue;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Restrictions;

namespace Intelibill.Licensing.Api.Restrictions;

internal static class ReopeningEndpoints
{
    private const int MaxRequestBytes = 16 * 1024;

    internal static void MapReopenings(this WebApplication app)
    {
        var routes = app.MapGroup("/api/admin/accounts/{account:guid}/closures/{closure:guid}/shops/{shop:guid}/reopening")
            .RequireAuthorization(ProviderSessions.DevicePolicy);
        routes.MapPost("/preview", PreviewAsync);
        routes.MapPost("", ReviewAsync);
    }

    private static async Task<IResult> PreviewAsync(HttpContext context, Guid account, Guid closure, Guid shop,
        AdminSignInHttp http, ReviewReopeningHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        var body = await CatalogueDraftEndpoints.ReadAsync<PreviewRequest>(context, MaxRequestBytes);
        if (body.Refusal is not null) return body.Refusal;
        if (!MatchesSession(context, body.Caller!)) return CatalogueDraftEndpoints.Problem(context, AdminRefreshErrors.SessionEnded);
        var result = await handler.PreviewAsync(body.Caller!, account, closure, shop, context.RequestAborted);
        if (result.IsError) return CatalogueDraftEndpoints.Problem(context, result.FirstError);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(result.Value);
    }

    private static async Task<IResult> ReviewAsync(HttpContext context, Guid account, Guid closure, Guid shop,
        AdminSignInHttp http, ReviewReopeningHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        var body = await CatalogueDraftEndpoints.ReadAsync<ReviewReopeningCommand>(context, MaxRequestBytes);
        if (body.Refusal is not null) return body.Refusal;
        if (!MatchesSession(context, body.Caller!)) return CatalogueDraftEndpoints.Problem(context, AdminRefreshErrors.SessionEnded);
        var result = await handler.ReviewAsync(body.Caller!, account, closure, shop, body.Value, CancellationToken.None);
        if (result.IsError) return CatalogueDraftEndpoints.Problem(context, result.FirstError);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(result.Value.Decision, statusCode: result.Value.Created ? 201 : 200);
    }

    private static bool MatchesSession(HttpContext context, AdminSessionCaller caller) =>
        !context.Request.Headers.TryGetValue("X-Admin-Session", out var header) ||
        Guid.TryParse(header.ToString(), out var session) && session == caller.SessionId;

    private sealed record PreviewRequest;
}
