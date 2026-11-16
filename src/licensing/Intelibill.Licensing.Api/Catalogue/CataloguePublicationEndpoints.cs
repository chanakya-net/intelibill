using ErrorOr;
using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Catalogue;

namespace Intelibill.Licensing.Api.Catalogue;

internal static class CataloguePublicationEndpoints
{
    internal static void MapCataloguePublication(this RouteGroupBuilder group)
    {
        const string trial = "/trial-policy/versions/{versionId:guid}";
        group.MapPost(trial + "/publish-preview", (HttpContext context, Guid versionId, AdminSignInHttp http,
            PublishTrialPolicyVersionHandler handler) => MutateAsync<PrepareCataloguePublication>(context, http,
                (caller, request, ct) => handler.PrepareAsync(caller, versionId, request, ct)));
        group.MapPost(trial + "/publish", (HttpContext context, Guid versionId, AdminSignInHttp http,
            PublishTrialPolicyVersionHandler handler) => MutateAsync<ConfirmCataloguePublication>(context, http,
                (caller, request, ct) => handler.ConfirmAsync(caller, versionId, request, ct)));
        const string plan = "/plans/{planId:guid}/versions/{versionId:guid}";
        group.MapPost(plan + "/publish-preview", (HttpContext context, Guid planId, Guid versionId, AdminSignInHttp http,
            PublishPlanVersionHandler handler) => MutateAsync<PrepareCataloguePublication>(context, http,
                (caller, request, ct) => handler.PrepareAsync(caller, planId, versionId, request, ct)));
        group.MapPost(plan + "/publish", (HttpContext context, Guid planId, Guid versionId, AdminSignInHttp http,
            PublishPlanVersionHandler handler) => MutateAsync<ConfirmCataloguePublication>(context, http,
                (caller, request, ct) => handler.ConfirmAsync(caller, planId, versionId, request, ct)));
        group.MapGet("/catalogue/history", HistoryAsync);
        group.MapGet("/catalogue/operations/{operationKey:guid}", RecoverAsync);
    }

    private static async Task<IResult> HistoryAsync(HttpContext context, ReadCatalogueHistoryHandler handler, Guid? after)
    {
        if (ProviderSessions.ReadCaller(context.User) is not { } caller)
            return CatalogueDraftEndpoints.Problem(context, AdminRefreshErrors.SessionEnded);
        var page = await handler.HandleAsync(caller, after, context.RequestAborted);
        return page.IsError ? CatalogueDraftEndpoints.Problem(context, page.FirstError) : Results.Ok(page.Value);
    }

    private static async Task<IResult> RecoverAsync(HttpContext context, Guid operationKey, CataloguePublicationOperation handler)
    {
        if (ProviderSessions.ReadCaller(context.User) is not { } caller)
            return CatalogueDraftEndpoints.Problem(context, AdminRefreshErrors.SessionEnded);
        return Respond(context, await handler.RecoverAsync(caller, operationKey, context.RequestAborted));
    }

    private static async Task<IResult> MutateAsync<T>(HttpContext context, AdminSignInHttp http,
        Func<AdminSessionCaller, T?, CancellationToken, Task<ErrorOr<CataloguePublicationView>>> execute) where T : class
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        var body = await CatalogueDraftEndpoints.ReadAsync<T>(context);
        if (body.Refusal is not null) return body.Refusal;
        return Respond(context, await execute(body.Caller!, body.Value, CancellationToken.None));
    }

    private static IResult Respond(HttpContext context, ErrorOr<CataloguePublicationView> result) =>
        result.IsError ? CatalogueDraftEndpoints.Problem(context, result.FirstError) : Results.Ok(result.Value);
}
