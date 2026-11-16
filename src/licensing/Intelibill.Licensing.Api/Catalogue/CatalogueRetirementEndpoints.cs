using ErrorOr;
using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Catalogue;

namespace Intelibill.Licensing.Api.Catalogue;

internal static class CatalogueRetirementEndpoints
{
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
        return Respond(context, await handler.HandleAsync(body.Caller!, planId, versionId, body.Value, CancellationToken.None));
    }

    private static async Task<IResult> RecoverAsync(HttpContext context, Guid operationKey, RetirePlanHandler handler)
    {
        if (ProviderSessions.ReadCaller(context.User) is not { } caller)
            return CatalogueDraftEndpoints.Problem(context, AdminRefreshErrors.SessionEnded);
        return Respond(context, await handler.RecoverAsync(caller, operationKey, context.RequestAborted));
    }

    private static IResult Respond(HttpContext context, ErrorOr<PlanRetirementView> result) =>
        result.IsError ? CatalogueDraftEndpoints.Problem(context, result.FirstError) : Results.Ok(result.Value);
}
