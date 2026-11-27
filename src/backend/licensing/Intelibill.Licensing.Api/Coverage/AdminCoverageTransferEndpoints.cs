using ErrorOr;
using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Api.Catalogue;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Coverage;

namespace Intelibill.Licensing.Api.Coverage;

internal static class AdminCoverageTransferEndpoints
{
    private const string SessionHeader = "X-Admin-Session";

    internal static void MapAdminCoverageTransfers(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/accounts/{account:guid}/coverage-transfers")
            .RequireAuthorization(ProviderSessions.GrantPolicy);
        group.MapGet("", ListAsync);
        group.MapPost("/{transferId:guid}/approve", ApproveAsync);
        group.MapGet("/{transferId:guid}/operations/{operationKey:guid}", RecoverAsync);
        group.MapPost("/{transferId:guid}/preview", PreviewAsync);
    }

    private static async Task<IResult> ListAsync(HttpContext context, Guid account,
        ApproveCoverageTransferHandler handler)
    {
        if (ProviderSessions.ReadCaller(context.User) is not { } caller || !MatchesSession(context, caller))
            return Problem(context, AdminRefreshErrors.SessionEnded);
        return Respond(context, await handler.ListAsync(caller, account, context.RequestAborted));
    }

    private static async Task<IResult> PreviewAsync(HttpContext context, Guid account, Guid transferId,
        AdminSignInHttp http, ApproveCoverageTransferHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        if (ProviderSessions.ReadCaller(context.User) is not { } caller || !MatchesSession(context, caller))
            return Problem(context, AdminRefreshErrors.SessionEnded);
        return Respond(context, await handler.PreviewAsync(caller, account, transferId, CancellationToken.None));
    }

    private static async Task<IResult> ApproveAsync(HttpContext context, Guid account, Guid transferId,
        AdminSignInHttp http, ApproveCoverageTransferHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        var body = await CatalogueDraftEndpoints.ReadAsync<TransferApprovalRequest>(context, 8192);
        if (body.Refusal is not null) return body.Refusal;
        if (!MatchesSession(context, body.Caller!)) return Problem(context, AdminRefreshErrors.SessionEnded);
        var result = await handler.ApproveAsync(body.Caller!, account, transferId, body.Value, CancellationToken.None);
        if (result.IsError) return Problem(context, result.FirstError);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(result.Value.Approval, statusCode: result.Value.Created ? 201 : 200);
    }

    private static async Task<IResult> RecoverAsync(HttpContext context, Guid account, Guid transferId,
        Guid operationKey, ApproveCoverageTransferHandler handler)
    {
        if (ProviderSessions.ReadCaller(context.User) is not { } caller || !MatchesSession(context, caller))
            return Problem(context, AdminRefreshErrors.SessionEnded);
        return Respond(context, await handler.RecoverAsync(caller, account, transferId, operationKey, context.RequestAborted));
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
