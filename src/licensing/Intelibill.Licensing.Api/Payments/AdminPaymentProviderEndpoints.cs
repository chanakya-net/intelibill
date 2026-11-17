using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Api.Catalogue;
using Intelibill.Licensing.Application.Payments;

namespace Intelibill.Licensing.Api.Payments;

internal static class AdminPaymentProviderEndpoints
{
    internal static void MapPaymentProviders(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/payment-providers").RequireAuthorization(ProviderSessions.PaymentPolicy);
        group.MapGet("", ReadAsync);
        group.MapPost("/selection", SelectAsync);
        group.MapGet("/operations/{key:guid}", RecoverAsync);
        app.MapGet("/api/admin/accounts/{account:guid}/orders/{order:guid}/payment-routing", DetailAsync)
            .RequireAuthorization(ProviderSessions.PaymentPolicy);
    }

    private static async Task<IResult> ReadAsync(HttpContext context, SelectPaymentProviderHandler handler) =>
        AdminManualPaymentEndpoints.Caller(context) is { } caller
            ? AdminManualPaymentEndpoints.Respond(context, await handler.StatusAsync(caller, context.RequestAborted))
            : AdminManualPaymentEndpoints.SessionEnded(context);

    private static async Task<IResult> SelectAsync(HttpContext context, AdminSignInHttp http, SelectPaymentProviderHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refusal) return refusal;
        if (AdminManualPaymentEndpoints.Caller(context) is not { } caller) return AdminManualPaymentEndpoints.SessionEnded(context);
        var body = await CatalogueDraftEndpoints.ReadAsync<SelectPaymentProvider>(context, 4096);
        return body.Refusal ?? AdminManualPaymentEndpoints.Respond(context,
            await handler.SelectAsync(caller, body.Value, CancellationToken.None));
    }

    private static async Task<IResult> RecoverAsync(HttpContext context, Guid key, SelectPaymentProviderHandler handler) =>
        AdminManualPaymentEndpoints.Caller(context) is { } caller
            ? AdminManualPaymentEndpoints.Respond(context, await handler.RecoverAsync(caller, key, context.RequestAborted))
            : AdminManualPaymentEndpoints.SessionEnded(context);

    private static async Task<IResult> DetailAsync(HttpContext context, Guid account, Guid order, SelectPaymentProviderHandler handler) =>
        AdminManualPaymentEndpoints.Caller(context) is { } caller
            ? AdminManualPaymentEndpoints.Respond(context, await handler.DetailAsync(caller, account, order, context.RequestAborted))
            : AdminManualPaymentEndpoints.SessionEnded(context);
}
