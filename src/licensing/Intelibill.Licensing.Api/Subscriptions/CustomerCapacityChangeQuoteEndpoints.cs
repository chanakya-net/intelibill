using ErrorOr;
using Intelibill.Licensing.Api.Billing;
using Intelibill.Licensing.Api.Status;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Subscriptions;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;

namespace Intelibill.Licensing.Api.Subscriptions;

/// <summary>Signed merchant delegation routes for owner capacity-increase previews (P06-01). Preview only.</summary>
internal static class CustomerCapacityChangeQuoteEndpoints
{
    internal static void MapCustomerCapacityChangeQuotes(this WebApplication app, AuthorizationPolicy policy)
    {
        var routes = app.MapGroup(MerchantBillingDelegationV1.AccountsPath +
            "/{account:guid}/subscriptions/{term:guid}/capacity-change-quotes").RequireAuthorization(policy);
        routes.MapPost("", CreateAsync);
        routes.MapGet("/{quote:guid}/shops", ShopsAsync);
        routes.MapGet("/{quote:guid}/shops/after/{after:guid}", ShopsAsync);
    }

    private static async Task<IResult> CreateAsync(HttpContext context, Guid account, Guid term,
        CreateCapacityChangeQuoteHandler handler, CancellationToken ct)
    {
        var proof = context.Features.Get<VerifiedBillingDelegation>();
        if (proof is null || proof.AccountId != account || !CustomerBillingEndpoints.MatchesBinding(context.Request, proof,
                CapacityChangeQuotesV1.Create, HttpMethods.Post, CapacityChangeQuotesV1.Path(account, term)) ||
            await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<CreateCapacityChangeQuoteV1>(
                context.Request, proof, CapacityChangeQuotesV1.MaxCreateBytes, ct) is not { } request)
            return Invalid(context);
        var result = await handler.HandleAsync(proof, account, term, request, ct);
        return result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Ok(result.Value.Quote);
    }

    private static async Task<IResult> ShopsAsync(HttpContext context, Guid account, Guid term, Guid quote,
        ReadCapacityChangeShopsHandler handler, CancellationToken ct, Guid? after = null) =>
        CustomerSubscriptionStatusEndpoints.ReadProof(context, account, CapacityChangeQuotesV1.Shops,
            CapacityChangeQuotesV1.ShopsPath(account, term, quote, after)) is { } proof
            ? Respond(context, await handler.HandleAsync(proof, account, term, quote, after, ct)) : Invalid(context);

    private static IResult Invalid(HttpContext context) =>
        CustomerBillingEndpoints.Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);

    private static IResult Respond<T>(HttpContext context, ErrorOr<T> result) =>
        result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Ok(result.Value);
}
