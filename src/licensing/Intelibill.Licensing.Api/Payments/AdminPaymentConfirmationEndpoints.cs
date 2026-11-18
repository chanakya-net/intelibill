using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Api.Catalogue;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Api.Payments;

internal static class AdminPaymentConfirmationEndpoints
{
    internal static void MapAdminPaymentConfirmations(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/accounts/{account:guid}/orders/{order:guid}/payment-confirmations")
            .RequireAuthorization(ProviderSessions.PaymentPolicy);
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}/document", DocumentAsync);
    }
    private static async Task<IResult> ListAsync(HttpContext context, Guid account, Guid order, ReadPaymentConfirmationHandler handler) =>
        AdminManualPaymentEndpoints.Caller(context) is { } caller
            ? AdminManualPaymentEndpoints.Respond(context, await handler.ListAsync(caller, account, order, context.RequestAborted))
            : AdminManualPaymentEndpoints.SessionEnded(context);
    private static async Task<IResult> DocumentAsync(HttpContext context, Guid account, Guid order, Guid id, ReadPaymentConfirmationHandler handler)
    {
        if (AdminManualPaymentEndpoints.Caller(context) is not { } caller) return AdminManualPaymentEndpoints.SessionEnded(context);
        var result = await handler.DocumentAsync(caller, account, order, id, context.RequestAborted);
        if (result.IsError) return CatalogueDraftEndpoints.Problem(context, result.FirstError);
        return Attachment(context, result.Value, id);
    }
    internal static IResult Attachment(HttpContext context, byte[] bytes, Guid id)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(bytes, PaymentConfirmationsV1.MediaType, PaymentConfirmationsV1.FileName(id));
    }
}
