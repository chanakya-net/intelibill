using ErrorOr;
using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Api.Catalogue;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Payments;

namespace Intelibill.Licensing.Api.Payments;

internal static class AdminManualPaymentEndpoints
{
    internal static void MapAdminManualPayments(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/accounts/{account:guid}/orders/{order:guid}/manual-payments")
            .RequireAuthorization(ProviderSessions.PaymentPolicy);
        group.MapGet("", ReadAsync);
        group.MapPost("/preview", PreviewAsync);
        group.MapPost("", ConfirmAsync);
        group.MapGet("/operations/{operationKey:guid}", RecoverAsync);
        group.MapGet("/receipts/{receipt:guid}/evidence", EvidenceAsync);
    }

    private static async Task<IResult> ReadAsync(HttpContext context, Guid account, Guid order, Guid? after,
        ConfirmManualPaymentHandler handler) =>
        Caller(context) is { } caller ? Respond(context, await handler.ReadAsync(caller, account, order, after, context.RequestAborted))
            : SessionEnded(context);

    private static async Task<IResult> PreviewAsync(HttpContext context, Guid account, Guid order, AdminSignInHttp http,
        ConfirmManualPaymentHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        if (Caller(context) is not { } caller) return SessionEnded(context);
        var body = await CatalogueDraftEndpoints.ReadAsync<ConfirmManualPayment>(context, 4096);
        return body.Refusal ?? Respond(context, await handler.PreviewAsync(caller, account, order, body.Value, context.RequestAborted));
    }

    private static async Task<IResult> ConfirmAsync(HttpContext context, Guid account, Guid order, AdminSignInHttp http,
        ConfirmManualPaymentHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        if (Caller(context) is not { } caller) return SessionEnded(context);
        var body = await CatalogueDraftEndpoints.ReadAsync<ConfirmManualPayment>(context, 4096);
        if (body.Refusal is not null) return body.Refusal;
        var result = await handler.ConfirmAsync(caller, account, order, body.Value, CancellationToken.None);
        if (result.IsError) return CatalogueDraftEndpoints.Problem(context, result.FirstError);
        return Respond<ManualPaymentReceipt>(context, result.Value.Receipt, result.Value.Created ? 201 : 200);
    }

    private static async Task<IResult> RecoverAsync(HttpContext context, Guid account, Guid order, Guid operationKey,
        ConfirmManualPaymentHandler handler) =>
        Caller(context) is { } caller
            ? Respond(context, await handler.RecoverAsync(caller, account, order, operationKey, context.RequestAborted))
            : SessionEnded(context);

    private static async Task<IResult> EvidenceAsync(HttpContext context, Guid account, Guid order, Guid receipt,
        ConfirmManualPaymentHandler handler) =>
        Caller(context) is { } caller
            ? Respond(context, await handler.EvidenceAsync(caller, account, order, receipt, context.RequestAborted))
            : SessionEnded(context);

    private static AdminSessionCaller? Caller(HttpContext context)
    {
        var caller = ProviderSessions.ReadCaller(context.User);
        return !context.Request.Headers.TryGetValue("X-Admin-Session", out var header) ||
            Guid.TryParse(header.ToString(), out var id) && id == caller?.SessionId ? caller : null;
    }

    private static IResult SessionEnded(HttpContext context) =>
        CatalogueDraftEndpoints.Problem(context, AdminRefreshErrors.SessionEnded);

    private static IResult Respond<T>(HttpContext context, ErrorOr<T> result, int status = 200)
    {
        context.Response.Headers.CacheControl = "no-store";
        return result.IsError ? CatalogueDraftEndpoints.Problem(context, result.FirstError) : Results.Json(result.Value, statusCode: status);
    }
}
