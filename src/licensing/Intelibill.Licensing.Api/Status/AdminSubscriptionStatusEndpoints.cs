using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Api.Catalogue;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Status;

namespace Intelibill.Licensing.Api.Status;

/// <summary>
/// Provider status reads through a real provider session only (never the local fixture scheme). The persisted
/// Support/Billing/Licensing Manager grant is re-read under lock in the handler; responses are no-store.
/// </summary>
internal static class AdminSubscriptionStatusEndpoints
{
    internal static void MapAdminSubscriptionStatus(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/accounts/{account:guid}").RequireAuthorization(ProviderSessions.StatusPolicy);
        group.MapGet("/status", ReadAccountAsync);
        group.MapGet("/closures", ReadClosuresAsync);
        group.MapGet("/subscriptions/{term:guid}/cancellation", ReadCancellationAsync);
        group.MapGet("/shops/{shop:guid}/status", ReadShopAsync);
    }

    private static async Task<IResult> ReadClosuresAsync(HttpContext context, Guid account, ReadProviderStatusHandler handler)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (ProviderSessions.ReadCaller(context.User) is not { } caller)
            return CatalogueDraftEndpoints.Problem(context, AdminRefreshErrors.SessionEnded);
        return Respond(context, await handler.ReadClosuresAsync(caller, account, context.RequestAborted));
    }

    private static async Task<IResult> ReadAccountAsync(HttpContext context, Guid account, Guid? after,
        ReadProviderStatusHandler handler)
    {
        if (ProviderSessions.ReadCaller(context.User) is not { } caller)
            return CatalogueDraftEndpoints.Problem(context, AdminRefreshErrors.SessionEnded);
        return Respond(context, await handler.ReadAccountAsync(caller, account, after, context.RequestAborted));
    }

    private static async Task<IResult> ReadCancellationAsync(HttpContext context, Guid account, Guid term, Guid? after,
        ReadProviderStatusHandler handler)
    {
        if (ProviderSessions.ReadCaller(context.User) is not { } caller)
            return CatalogueDraftEndpoints.Problem(context, AdminRefreshErrors.SessionEnded);
        return Respond(context, await handler.ReadCancellationAsync(caller, account, term, after, context.RequestAborted));
    }

    private static async Task<IResult> ReadShopAsync(HttpContext context, Guid account, Guid shop, ReadProviderStatusHandler handler)
    {
        if (ProviderSessions.ReadCaller(context.User) is not { } caller)
            return CatalogueDraftEndpoints.Problem(context, AdminRefreshErrors.SessionEnded);
        return Respond(context, await handler.ReadShopAsync(caller, account, shop, context.RequestAborted));
    }

    private static IResult Respond<T>(HttpContext context, ErrorOr.ErrorOr<T> result)
    {
        context.Response.Headers.CacheControl = "no-store";
        return result.IsError ? CatalogueDraftEndpoints.Problem(context, result.FirstError) : Results.Json(result.Value);
    }
}
