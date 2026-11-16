using ErrorOr;
using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Catalogue;

namespace Intelibill.Licensing.Api.Catalogue;

internal static class CatalogueRetirementEndpoints
{
    private const string SessionHeader = "X-Admin-Session";

    internal static void MapCatalogueRetirement(this RouteGroupBuilder group)
    {
        group.MapPost("/plans/{planId:guid}/versions/{versionId:guid}/retire", RetireAsync);
        group.MapGet("/catalogue/retirements/{operationKey:guid}", RecoverAsync);
    }

    private static async Task<IResult> RetireAsync(HttpContext context, Guid planId, Guid versionId,
        AdminSignInHttp http, RetirePlanHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        var body = await CatalogueDraftEndpoints.ReadAsync<RetirePlanVersion>(context);
        if (body.Refusal is not null) return body.Refusal;
        var caller = body.Caller!;
        if (!MatchesSession(context, caller)) return CatalogueDraftEndpoints.Problem(context, AdminRefreshErrors.SessionEnded);
        return Respond(context, await handler.HandleAsync(caller, planId, versionId, body.Value, CancellationToken.None));
    }

    private static async Task<IResult> RecoverAsync(HttpContext context, Guid operationKey, RetirePlanHandler handler)
    {
        if (ProviderSessions.ReadCaller(context.User) is not { } caller || !MatchesSession(context, caller))
            return CatalogueDraftEndpoints.Problem(context, AdminRefreshErrors.SessionEnded);
        return Respond(context, await handler.RecoverAsync(caller, operationKey, context.RequestAborted));
    }

    // Console requests pin the session confirmed by the tab so a cookie change cannot reassign a request.
    private static bool MatchesSession(HttpContext context, AdminSessionCaller caller) =>
        !context.Request.Headers.TryGetValue(SessionHeader, out var value)
        || Guid.TryParse(value.ToString(), out var sessionId) && sessionId == caller.SessionId;

    private static IResult Respond(HttpContext context, ErrorOr<PlanRetirementView> result) =>
        result.IsError ? CatalogueDraftEndpoints.Problem(context, result.FirstError) : Results.Ok(result.Value);
}
