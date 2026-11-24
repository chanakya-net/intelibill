using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Api.Catalogue;
using Intelibill.Licensing.Api.Payments;
using Intelibill.Licensing.Application.Refunds;

namespace Intelibill.Licensing.Api.Refunds;

/// <summary>
/// Per-account provider view of customer refund requests: read-only intake (P06-08) and the Billing approval of one
/// request with its reserved amount and explicit access treatment (P06-09). Every answer is no-store.
/// </summary>
internal static class AdminRefundRequestEndpoints
{
    internal static void MapAdminRefundRequests(this WebApplication app)
    {
        var routes = app.MapGroup("/api/admin/accounts/{account:guid}/refund-requests")
            .RequireAuthorization(ProviderSessions.PaymentPolicy);
        routes.MapGet("", IntakeAsync);
        routes.MapPost("/{request:guid}/approval-preview", PreviewAsync);
        routes.MapPost("/{request:guid}/approvals", ApproveAsync);
        routes.MapGet("/{request:guid}/approvals/operations/{operationKey:guid}", RecoverAsync);
    }

    private static async Task<IResult> IntakeAsync(
        HttpContext context, Guid account, Guid? after, Guid? request, ReadRefundRequestsHandler handler) =>
        AdminManualPaymentEndpoints.Caller(context) is { } caller
            ? AdminManualPaymentEndpoints.Respond(context, await handler.IntakeAsync(caller, account, after, context.RequestAborted, request))
            : AdminManualPaymentEndpoints.SessionEnded(context);

    private static async Task<IResult> PreviewAsync(HttpContext context, Guid account, Guid request,
        AdminSignInHttp http, ApproveRefundHandler handler)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        if (AdminManualPaymentEndpoints.Caller(context) is not { } caller)
            return AdminManualPaymentEndpoints.SessionEnded(context);
        var body = await CatalogueDraftEndpoints.ReadAsync<PreviewRefundApproval>(context, RefundApprovalRules.MaxCommandBytes);
        if (body.Refusal is not null) return body.Refusal;
        var result = await handler.PreviewAsync(caller, account, request, body.Value, context.RequestAborted);
        if (result.IsError && result.FirstError.Metadata?.GetValueOrDefault("remainingAmountPaise") is long remaining)
            return Results.Problem(statusCode: 409, title: result.FirstError.Description,
                extensions: new Dictionary<string, object?> { ["code"] = result.FirstError.Code,
                    ["traceId"] = context.TraceIdentifier, ["remainingAmountPaise"] = remaining });
        return AdminManualPaymentEndpoints.Respond(context, result);
    }

    // The write is never cancelled by a dropped connection: a committed approval stays recoverable by its key.
    private static async Task<IResult> ApproveAsync(HttpContext context, Guid account, Guid request,
        AdminSignInHttp http, ApproveRefundHandler handler)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        if (AdminManualPaymentEndpoints.Caller(context) is not { } caller)
            return AdminManualPaymentEndpoints.SessionEnded(context);
        var body = await CatalogueDraftEndpoints.ReadAsync<ApproveRefund>(context, RefundApprovalRules.MaxCommandBytes);
        if (body.Refusal is not null) return body.Refusal;
        var result = await handler.ApproveAsync(caller, account, request, body.Value, CancellationToken.None);
        if (result.IsError) return CatalogueDraftEndpoints.Problem(context, result.FirstError);
        return AdminManualPaymentEndpoints.Respond<RefundApprovalReceipt>(context, result.Value.Receipt,
            result.Value.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
    }

    private static async Task<IResult> RecoverAsync(HttpContext context, Guid account, Guid request, Guid operationKey,
        ApproveRefundHandler handler) =>
        AdminManualPaymentEndpoints.Caller(context) is { } caller
            ? AdminManualPaymentEndpoints.Respond(context,
                await handler.RecoverAsync(caller, account, request, operationKey, context.RequestAborted))
            : AdminManualPaymentEndpoints.SessionEnded(context);
}
