using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Api.Payments;
using Intelibill.Licensing.Application.Refunds;

namespace Intelibill.Licensing.Api.Refunds;

/// <summary>Read-only per-account provider intake view of customer refund requests (P06-08).</summary>
internal static class AdminRefundRequestEndpoints
{
    internal static void MapAdminRefundRequests(this WebApplication app)
    {
        var routes = app.MapGroup("/api/admin/accounts/{account:guid}/refund-requests")
            .RequireAuthorization(ProviderSessions.PaymentPolicy);
        routes.MapGet("", IntakeAsync);
    }

    private static async Task<IResult> IntakeAsync(
        HttpContext context, Guid account, Guid? after, ReadRefundRequestsHandler handler) =>
        AdminManualPaymentEndpoints.Caller(context) is { } caller
            ? AdminManualPaymentEndpoints.Respond(context, await handler.IntakeAsync(caller, account, after, context.RequestAborted))
            : AdminManualPaymentEndpoints.SessionEnded(context);
}
