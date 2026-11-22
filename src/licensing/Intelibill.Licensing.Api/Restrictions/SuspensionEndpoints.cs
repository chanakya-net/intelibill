using ErrorOr;
using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Api.Catalogue;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Restrictions;

namespace Intelibill.Licensing.Api.Restrictions;

internal static class SuspensionEndpoints
{
    private const int MaxRequestBytes = 16 * 1024;

    internal static void MapSuspensions(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/accounts/{account:guid}/restrictions")
            .RequireAuthorization(ProviderSessions.DevicePolicy);
        group.MapPost("/preview", PreviewAsync);
        group.MapPost("", SuspendAsync);
        group.MapPost("/{id:guid}/release/preview", PreviewReleaseAsync);
        group.MapPost("/{id:guid}/release", ReleaseAsync);
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", ReadAsync);
    }

    private static async Task<IResult> PreviewAsync(HttpContext context, Guid account, AdminSignInHttp http,
        SuspendAccessHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        var body = await CatalogueDraftEndpoints.ReadAsync<SuspensionSelection>(context, MaxRequestBytes);
        if (body.Refusal is not null) return body.Refusal;
        if (!MatchesSession(context, body.Caller!)) return InvalidSession(context);
        return Respond(context, await handler.PreviewAsync(body.Caller!, account, body.Value, context.RequestAborted));
    }

    private static async Task<IResult> SuspendAsync(HttpContext context, Guid account, AdminSignInHttp http,
        SuspendAccessHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        var body = await CatalogueDraftEndpoints.ReadAsync<SuspendAccessCommand>(context, MaxRequestBytes);
        if (body.Refusal is not null) return body.Refusal;
        if (!MatchesSession(context, body.Caller!)) return InvalidSession(context);
        var result = await handler.SuspendAsync(body.Caller!, account, body.Value, CancellationToken.None);
        return result.IsError ? CatalogueDraftEndpoints.Problem(context, result.FirstError)
            : Respond<SuspensionStatus>(context, result.Value.Status, result.Value.Created ? 201 : 200);
    }

    private static async Task<IResult> PreviewReleaseAsync(HttpContext context, Guid account, Guid id, AdminSignInHttp http,
        ReleaseSuspensionHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        var body = await CatalogueDraftEndpoints.ReadAsync<ReleasePreviewRequest>(context, MaxRequestBytes);
        if (body.Refusal is not null) return body.Refusal;
        if (!MatchesSession(context, body.Caller!)) return InvalidSession(context);
        return Respond(context, await handler.PreviewAsync(body.Caller!, account, id, context.RequestAborted));
    }

    private static async Task<IResult> ReleaseAsync(HttpContext context, Guid account, Guid id, AdminSignInHttp http,
        ReleaseSuspensionHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        var body = await CatalogueDraftEndpoints.ReadAsync<ReleaseSuspensionCommand>(context, MaxRequestBytes);
        if (body.Refusal is not null) return body.Refusal;
        if (!MatchesSession(context, body.Caller!)) return InvalidSession(context);
        var result = await handler.ReleaseAsync(body.Caller!, account, id, body.Value, CancellationToken.None);
        return result.IsError ? CatalogueDraftEndpoints.Problem(context, result.FirstError)
            : Respond<SuspensionStatus>(context, result.Value.Status, result.Value.Created ? 201 : 200);
    }

    private sealed record ReleasePreviewRequest;

    private static async Task<IResult> ListAsync(HttpContext context, Guid account, SuspendAccessHandler handler) =>
        ProviderSessions.ReadCaller(context.User) is { } caller && MatchesSession(context, caller)
            ? Respond(context, await handler.ListAsync(caller, account, context.RequestAborted)) : InvalidSession(context);

    private static async Task<IResult> ReadAsync(HttpContext context, Guid account, Guid id, SuspendAccessHandler handler) =>
        ProviderSessions.ReadCaller(context.User) is { } caller && MatchesSession(context, caller)
            ? Respond(context, await handler.ReadAsync(caller, account, id, context.RequestAborted)) : InvalidSession(context);

    private static bool MatchesSession(HttpContext context, AdminSessionCaller caller) =>
        !context.Request.Headers.TryGetValue("X-Admin-Session", out var header)
        || Guid.TryParse(header.ToString(), out var sessionId) && sessionId == caller.SessionId;

    private static IResult InvalidSession(HttpContext context) =>
        CatalogueDraftEndpoints.Problem(context, AdminRefreshErrors.SessionEnded);

    private static IResult Respond<T>(HttpContext context, ErrorOr<T> result, int status = 200)
    {
        if (result.IsError) return CatalogueDraftEndpoints.Problem(context, result.FirstError);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(result.Value, statusCode: status);
    }
}
