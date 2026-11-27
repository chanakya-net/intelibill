using ErrorOr;
using Intelibill.Licensing.Api.Billing;
using Intelibill.Licensing.Api.Status;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;

namespace Intelibill.Licensing.Api.Payments;

internal static class CustomerCheckoutEndpoints
{
    internal static void MapCustomerCheckout(this WebApplication app, AuthorizationPolicy policy)
    {
        var group = app.MapGroup(MerchantBillingDelegationV1.AccountsPath + "/{account:guid}/orders").RequireAuthorization(policy);
        group.MapPost("", CreateAsync);
        group.MapGet("/{order:guid}", DetailAsync);
        group.MapPost("/{order:guid}/accept", AcceptAsync);
        group.MapGet("/operations/{kind}/{key:guid}", RecoverAsync);
        group.MapGet("/eligible-shops", CandidatesAsync);
        group.MapGet("/eligible-shops/after/{after:guid}", CandidatesAsync);
        group.MapGet("/{order:guid}/shops", ShopsAsync);
        group.MapGet("/{order:guid}/shops/after/{after:guid}", ShopsAsync);
    }

    private static async Task<IResult> CreateAsync(HttpContext context, Guid account, CreateCheckoutOrderHandler handler,
        CancellationToken ct)
    {
        var proof = PostProof(context, account, CheckoutOrdersV1.Create, CheckoutOrdersV1.Path(account));
        if (proof is null || await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<CreateCheckoutQuoteV1>(
                context.Request, proof, CheckoutOrdersV1.MaxCreateBytes, ct) is not { } request)
            return Invalid(context);

        var result = await handler.HandleAsync(proof, account, request, ct);
        return result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError)
            : Results.Json(result.Value.Quote, statusCode: result.Value.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
    }

    private static async Task<IResult> AcceptAsync(HttpContext context, Guid account, Guid order,
        AcceptCheckoutOrderHandler handler, CancellationToken ct)
    {
        var proof = PostProof(context, account, CheckoutOrdersV1.Accept, CheckoutOrdersV1.AcceptPath(account, order));
        if (proof is null || await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<AcceptCheckoutQuoteV1>(
                context.Request, proof, CheckoutOrdersV1.MaxAcceptBytes, ct) is not { } request)
            return Invalid(context);

        return Respond(context, await handler.HandleAsync(proof, account, order, request, ct));
    }

    private static async Task<IResult> DetailAsync(HttpContext context, Guid account, Guid order,
        ReadCheckoutOrdersHandler handler, CancellationToken ct) =>
        ReadProof(context, account, CheckoutOrdersV1.Read, CheckoutOrdersV1.DetailPath(account, order)) is { } proof
            ? Respond(context, await handler.ReadAsync(proof, account, order, null, ct)) : Invalid(context);

    private static async Task<IResult> RecoverAsync(HttpContext context, Guid account, string kind, Guid key,
        ReadCheckoutOrdersHandler handler, CancellationToken ct) =>
        ReadProof(context, account, CheckoutOrdersV1.Recover, CheckoutOrdersV1.OperationPath(account, kind, key)) is { } proof
            ? Respond(context, await handler.ReadAsync(proof, account, key, kind, ct)) : Invalid(context);

    private static async Task<IResult> CandidatesAsync(HttpContext context, Guid account,
        ReadCheckoutOrdersHandler handler, CancellationToken ct, Guid? after = null) =>
        ReadProof(context, account, CheckoutOrdersV1.Candidates, CheckoutOrdersV1.CandidatesPath(account, after)) is { } proof
            ? Respond(context, await handler.PageAsync(proof, account, null, after, ct)) : Invalid(context);

    private static async Task<IResult> ShopsAsync(HttpContext context, Guid account, Guid order,
        ReadCheckoutOrdersHandler handler, CancellationToken ct, Guid? after = null) =>
        ReadProof(context, account, CheckoutOrdersV1.Shops, CheckoutOrdersV1.ShopsPath(account, order, after)) is { } proof
            ? Respond(context, await handler.PageAsync(proof, account, order, after, ct)) : Invalid(context);

    private static VerifiedBillingDelegation? ReadProof(HttpContext context, Guid account, string action, string path) =>
        CustomerSubscriptionStatusEndpoints.ReadProof(context, account, action, path);

    private static VerifiedBillingDelegation? PostProof(HttpContext context, Guid account, string action, string path) =>
        context.Features.Get<VerifiedBillingDelegation>() is { } proof && proof.AccountId == account &&
        CustomerBillingEndpoints.MatchesBinding(context.Request, proof, action, HttpMethods.Post, path) ? proof : null;

    private static IResult Invalid(HttpContext context) =>
        CustomerBillingEndpoints.Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);

    private static IResult Respond<T>(HttpContext context, ErrorOr<T> result) =>
        result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Ok(result.Value);
}
