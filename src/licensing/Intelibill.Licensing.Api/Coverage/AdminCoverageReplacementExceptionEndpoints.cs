using ErrorOr;
using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Api.Catalogue;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Coverage;

namespace Intelibill.Licensing.Api.Coverage;

/// <summary>
/// Provider early coverage-replacement review and approval through a real provider session only.
/// Writes require permitted admin origin, CSRF proof, pinned session header and strict bounded JSON;
/// persisted Licensing Manager capability and recent MFA are re-verified inside the handler transaction.
/// </summary>
internal static class AdminCoverageReplacementExceptionEndpoints
{
    private const string SessionHeader = "X-Admin-Session";
    private const int MaxRequestBytes = 8 * 1024;

    internal static void MapAdminCoverageReplacementExceptions(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/accounts/{account:guid}/coverage-replacement-exceptions")
            .RequireAuthorization(ProviderSessions.GrantPolicy);
        group.MapGet("", ListAsync);
        group.MapPost("/{exceptionId:guid}/preview", PreviewAsync);
        group.MapPost("/{exceptionId:guid}/approve", ApproveAsync);
        group.MapGet("/{exceptionId:guid}/operations/{operationKey:guid}", RecoverAsync);
    }

    private static async Task<IResult> ListAsync(HttpContext context, Guid account,
        ApproveCoverageReplacementHandler handler)
    {
        if (ProviderSessions.ReadCaller(context.User) is not { } caller || !MatchesSession(context, caller))
            return Problem(context, AdminRefreshErrors.SessionEnded);
        return Respond(context, await handler.ListAsync(caller, account, context.RequestAborted));
    }

    private static async Task<IResult> PreviewAsync(HttpContext context, Guid account, Guid exceptionId,
        AdminSignInHttp http, ApproveCoverageReplacementHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        if (ProviderSessions.ReadCaller(context.User) is not { } caller || !MatchesSession(context, caller))
            return Problem(context, AdminRefreshErrors.SessionEnded);
        return Respond(context, await handler.PreviewAsync(caller, account, exceptionId, CancellationToken.None));
    }

    private static async Task<IResult> ApproveAsync(HttpContext context, Guid account, Guid exceptionId,
        AdminSignInHttp http, ApproveCoverageReplacementHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        var body = await CatalogueDraftEndpoints.ReadAsync<ApproveCoverageReplacementRequestV1>(context, MaxRequestBytes);
        if (body.Refusal is not null) return body.Refusal;
        if (!MatchesSession(context, body.Caller!)) return Problem(context, AdminRefreshErrors.SessionEnded);
        var result = await handler.ApproveAsync(body.Caller!, account, exceptionId, body.Value, CancellationToken.None);
        if (result.IsError) return Problem(context, result.FirstError);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(result.Value.Approval, statusCode: result.Value.Created ? 201 : 200);
    }

    private static async Task<IResult> RecoverAsync(HttpContext context, Guid account, Guid exceptionId,
        Guid operationKey, ApproveCoverageReplacementHandler handler)
    {
        if (ProviderSessions.ReadCaller(context.User) is not { } caller || !MatchesSession(context, caller))
            return Problem(context, AdminRefreshErrors.SessionEnded);
        return Respond(context, await handler.RecoverAsync(caller, account, exceptionId, operationKey, context.RequestAborted));
    }

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
