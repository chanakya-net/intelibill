using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Api.Catalogue;
using Intelibill.Licensing.Application.Payments;

namespace Intelibill.Licensing.Api.Payments;

internal static class AdminPaymentReviewEndpoints
{
    internal static void MapAdminPaymentReviews(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/accounts/{account:guid}/payment-reviews")
            .RequireAuthorization(ProviderSessions.PaymentPolicy);
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", DetailAsync);
        group.MapPost("/{id:guid}/decisions", DecideAsync);
        group.MapGet("/{id:guid}/operations/{key:guid}", RecoverAsync);
    }

    private static async Task<IResult> ListAsync(HttpContext context, Guid account, Guid? order, Guid? after, IPaymentReviewStore store) =>
        AdminManualPaymentEndpoints.Caller(context) is { } caller
            ? AdminManualPaymentEndpoints.Respond(context, await store.ListAsync(caller, account, order, after, context.RequestAborted))
            : AdminManualPaymentEndpoints.SessionEnded(context);

    private static async Task<IResult> DetailAsync(HttpContext context, Guid account, Guid id, Guid? after,
        ReviewUnmatchedPaymentHandler handler) =>
        AdminManualPaymentEndpoints.Caller(context) is { } caller
            ? AdminManualPaymentEndpoints.Respond(context, await handler.ReadAsync(caller, account, id, after, context.RequestAborted))
            : AdminManualPaymentEndpoints.SessionEnded(context);

    private static async Task<IResult> RecoverAsync(HttpContext context, Guid account, Guid id, Guid key,
        ReviewUnmatchedPaymentHandler handler) =>
        AdminManualPaymentEndpoints.Caller(context) is { } caller
            ? AdminManualPaymentEndpoints.Respond(context, await handler.RecoverAsync(caller, account, id, key, context.RequestAborted))
            : AdminManualPaymentEndpoints.SessionEnded(context);

    private static async Task<IResult> DecideAsync(HttpContext context, Guid account, Guid id, AdminSignInHttp http,
        ReviewUnmatchedPaymentHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        if (AdminManualPaymentEndpoints.Caller(context) is not { } caller)
            return AdminManualPaymentEndpoints.SessionEnded(context);
        var body = await CatalogueDraftEndpoints.ReadAsync<ReviewPaymentCommand>(context, 4096);
        if (body.Refusal is not null) return body.Refusal;
        var result = await handler.DecideAsync(caller, account, id, body.Value, CancellationToken.None);
        return result.IsError ? CatalogueDraftEndpoints.Problem(context, result.FirstError)
            : AdminManualPaymentEndpoints.Respond<PaymentReviewDecision>(context, result.Value.Decision, result.Value.Created ? 201 : 200);
    }
}
