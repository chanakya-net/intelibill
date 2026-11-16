using ErrorOr;
using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Api.Catalogue;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Trials;

namespace Intelibill.Licensing.Api.Trials;

internal static class TrialExtensionEndpoints
{
    private const string SessionHeader = "X-Admin-Session";
    private const int MaxRequestBytes = 16 * 1024;

    internal static void MapTrialExtensions(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/accounts/{account:guid}/shops/{shop:guid}/trial-extensions")
            .RequireAuthorization(ProviderSessions.GrantPolicy);
        group.MapPost("/preview", PreviewAsync);
        group.MapPost("", ExtendAsync);
        group.MapGet("/operations/{operationKey:guid}", RecoverAsync);
    }

    private static async Task<IResult> PreviewAsync(HttpContext context, Guid account, Guid shop, AdminSignInHttp http,
        ExtendTrialHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        var body = await CatalogueDraftEndpoints.ReadAsync<TrialExtensionDraftV1>(context, MaxRequestBytes);
        if (body.Refusal is not null) return body.Refusal;
        if (!MatchesSession(context, body.Caller!)) return Problem(context, AdminRefreshErrors.SessionEnded);
        return Respond(context, await handler.PreviewAsync(body.Caller!, account, shop, body.Value, CancellationToken.None));
    }

    private static async Task<IResult> ExtendAsync(HttpContext context, Guid account, Guid shop, AdminSignInHttp http,
        ExtendTrialHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        var body = await CatalogueDraftEndpoints.ReadAsync<ConfirmTrialExtensionV1>(context, MaxRequestBytes);
        if (body.Refusal is not null) return body.Refusal;
        if (!MatchesSession(context, body.Caller!)) return Problem(context, AdminRefreshErrors.SessionEnded);
        var result = await handler.ExtendAsync(body.Caller!, account, shop, body.Value, CancellationToken.None);
        if (result.IsError) return Problem(context, result.FirstError);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(result.Value.Result, statusCode: result.Value.Created ? 201 : 200);
    }

    private static async Task<IResult> RecoverAsync(HttpContext context, Guid account, Guid shop, Guid operationKey,
        ExtendTrialHandler handler)
    {
        if (ProviderSessions.ReadCaller(context.User) is not { } caller || !MatchesSession(context, caller))
            return Problem(context, AdminRefreshErrors.SessionEnded);
        return Respond(context, await handler.RecoverAsync(caller, account, shop, operationKey, context.RequestAborted));
    }

    // Console requests pin the session confirmed by the tab so a cookie change cannot reassign a request.
    private static bool MatchesSession(HttpContext context, AdminSessionCaller caller) =>
        !context.Request.Headers.TryGetValue(SessionHeader, out var value)
        || Guid.TryParse(value.ToString(), out var sessionId) && sessionId == caller.SessionId;

    private static IResult Respond<T>(HttpContext context, ErrorOr<T> result)
    {
        if (result.IsError) return Problem(context, result.FirstError);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(result.Value);
    }

    private static IResult Problem(HttpContext context, Error error) => CatalogueDraftEndpoints.Problem(context, error);
}
