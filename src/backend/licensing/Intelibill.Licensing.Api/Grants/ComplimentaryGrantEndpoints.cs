using ErrorOr;
using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Api.Catalogue;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Grants;

namespace Intelibill.Licensing.Api.Grants;

/// <summary>
/// Provider complimentary grants through a real provider session only (never the local fixture scheme). Writes
/// need the admin origin, CSRF proof and strict bounded JSON; a pinned console session header must match the
/// cookie's session. The persisted Licensing Manager grant and recent MFA are re-checked in the handler.
/// </summary>
internal static class ComplimentaryGrantEndpoints
{
    private const string SessionHeader = "X-Admin-Session";
    private const int MaxRequestBytes = 16 * 1024;

    internal static void MapComplimentaryGrants(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/accounts/{account:guid}/grants/complimentary")
            .RequireAuthorization(ProviderSessions.GrantPolicy);
        group.MapPost("/preview", PreviewAsync);
        group.MapPost("", GrantAsync);
        group.MapGet("/operations/{operationKey:guid}", RecoverAsync);
    }

    private static async Task<IResult> PreviewAsync(HttpContext context, Guid account, AdminSignInHttp http,
        GrantComplimentaryAccessHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        var body = await CatalogueDraftEndpoints.ReadAsync<ComplimentaryGrantDraftV1>(context, MaxRequestBytes);
        if (body.Refusal is not null) return body.Refusal;
        if (!MatchesSession(context, body.Caller!)) return Problem(context, AdminRefreshErrors.SessionEnded);
        return Respond(context, await handler.PreviewAsync(body.Caller!, account, body.Value, CancellationToken.None));
    }

    private static async Task<IResult> GrantAsync(HttpContext context, Guid account, AdminSignInHttp http,
        GrantComplimentaryAccessHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        var body = await CatalogueDraftEndpoints.ReadAsync<ConfirmComplimentaryGrantV1>(context, MaxRequestBytes);
        if (body.Refusal is not null) return body.Refusal;
        if (!MatchesSession(context, body.Caller!)) return Problem(context, AdminRefreshErrors.SessionEnded);
        var result = await handler.GrantAsync(body.Caller!, account, body.Value, CancellationToken.None);
        if (result.IsError) return Problem(context, result.FirstError);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(result.Value.Result, statusCode: result.Value.Created ? 201 : 200);
    }

    private static async Task<IResult> RecoverAsync(HttpContext context, Guid account, Guid operationKey,
        GrantComplimentaryAccessHandler handler)
    {
        if (ProviderSessions.ReadCaller(context.User) is not { } caller || !MatchesSession(context, caller))
            return Problem(context, AdminRefreshErrors.SessionEnded);
        return Respond(context, await handler.RecoverAsync(caller, account, operationKey, context.RequestAborted));
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
