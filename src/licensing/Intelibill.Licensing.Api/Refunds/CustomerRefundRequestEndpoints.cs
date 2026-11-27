using ErrorOr;
using Intelibill.Licensing.Api.Billing;
using Intelibill.Licensing.Api.Status;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Refunds;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;

namespace Intelibill.Licensing.Api.Refunds;

/// <summary>Signed merchant delegation routes for billing-member refund requests (P06-08).</summary>
internal static class CustomerRefundRequestEndpoints
{
    internal static void MapCustomerRefundRequests(this WebApplication app, AuthorizationPolicy policy)
    {
        var routes = app.MapGroup(MerchantBillingDelegationV1.AccountsPath + "/{account:guid}/refund-requests")
            .RequireAuthorization(policy);
        routes.MapPost("", CreateAsync);
        routes.MapGet("", ListAsync);
        routes.MapGet("/after/{after:guid}", ListAsync);
    }

    private static async Task<IResult> CreateAsync(HttpContext context, Guid account,
        CreateRefundRequestHandler handler, CancellationToken ct)
    {
        var proof = context.Features.Get<VerifiedBillingDelegation>();
        if (proof is null || proof.AccountId != account || !CustomerBillingEndpoints.MatchesBinding(context.Request, proof,
                RefundRequestsV1.Create, HttpMethods.Post, RefundRequestsV1.Path(account)) ||
            await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<CreateRefundRequestV1>(
                context.Request, proof, RefundRequestsV1.MaxCreateBytes, ct) is not { } request)
            return Invalid(context);
        return Respond(context, await handler.HandleAsync(proof, account, request, ct));
    }

    private static async Task<IResult> ListAsync(HttpContext context, Guid account,
        ReadRefundRequestsHandler handler, CancellationToken ct, Guid? after = null) =>
        CustomerSubscriptionStatusEndpoints.ReadProof(context, account, RefundRequestsV1.List,
            RefundRequestsV1.ListPath(account, after)) is { } proof
            ? Respond(context, await handler.ListAsync(proof, account, after, ct)) : Invalid(context);

    private static IResult Invalid(HttpContext context) =>
        CustomerBillingEndpoints.Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);

    private static IResult Respond<T>(HttpContext context, ErrorOr<T> result) =>
        result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Ok(result.Value);
}
