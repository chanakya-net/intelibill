using ErrorOr;
using Intelibill.Licensing.Api.Billing;
using Intelibill.Licensing.Api.Status;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Changes;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;

namespace Intelibill.Licensing.Api.Subscriptions;

/// <summary>Signed merchant delegation routes for owner-selected capacity reductions at renewal (P06-03).</summary>
internal static class CustomerCapacityReductionEndpoints
{
    internal static void MapCustomerCapacityReductions(this WebApplication app, AuthorizationPolicy policy)
    {
        var routes = app.MapGroup(MerchantBillingDelegationV1.AccountsPath +
            "/{account:guid}/subscriptions/{term:guid}/capacity-reductions").RequireAuthorization(policy);
        routes.MapPost("/previews", PreviewAsync);
        routes.MapGet("/previews/{preview:guid}/shops", PreviewShopsAsync);
        routes.MapGet("/previews/{preview:guid}/shops/after/{after:guid}", PreviewShopsAsync);
        routes.MapPost("", ScheduleAsync);
        routes.MapGet("", ReadAsync);
        routes.MapGet("/operations/{key:guid}", RecoverAsync);
        routes.MapGet("/{schedule:guid}/shops", ShopsAsync);
        routes.MapGet("/{schedule:guid}/shops/after/{after:guid}", ShopsAsync);
        routes.MapPost("/{schedule:guid}/order", OrderAsync);
        routes.MapPost("/{schedule:guid}/resolve", ResolveAsync);
    }

    private static async Task<IResult> PreviewAsync(HttpContext context, Guid account, Guid term,
        ScheduleCapacityReductionHandler handler, CancellationToken ct)
    {
        var proof = context.Features.Get<VerifiedBillingDelegation>();
        if (proof is null || proof.AccountId != account || !CustomerBillingEndpoints.MatchesBinding(context.Request, proof,
                CapacityReductionsV1.Preview, HttpMethods.Post, CapacityReductionsV1.PreviewsPath(account, term)) ||
            await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<CreateCapacityReductionPreviewV1>(
                context.Request, proof, CapacityReductionsV1.MaxPreviewCreateBytes, ct) is not { } request)
            return Invalid(context);
        var result = await handler.PreviewAsync(proof, account, term, request, ct);
        return result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Ok(result.Value.Preview);
    }

    private static async Task<IResult> PreviewShopsAsync(HttpContext context, Guid account, Guid term, Guid preview,
        ScheduleCapacityReductionHandler handler, CancellationToken ct, Guid? after = null) =>
        CustomerSubscriptionStatusEndpoints.ReadProof(context, account, CapacityReductionsV1.PreviewShops,
            CapacityReductionsV1.PreviewShopsPath(account, term, preview, after)) is { } proof
            ? Respond(context, await handler.PreviewShopsAsync(proof, account, term, preview, after, ct)) : Invalid(context);

    private static async Task<IResult> ScheduleAsync(HttpContext context, Guid account, Guid term,
        ScheduleCapacityReductionHandler handler, CancellationToken ct)
    {
        var proof = context.Features.Get<VerifiedBillingDelegation>();
        if (proof is null || proof.AccountId != account || !CustomerBillingEndpoints.MatchesBinding(context.Request, proof,
                CapacityReductionsV1.Schedule, HttpMethods.Post, CapacityReductionsV1.BasePath(account, term)) ||
            await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<ScheduleCapacityReductionV1>(
                context.Request, proof, CapacityReductionsV1.MaxScheduleBytes, ct) is not { } request)
            return Invalid(context);
        var result = await handler.ScheduleAsync(proof, account, term, request, ct);
        return result.IsError
            ? CustomerBillingEndpoints.Failure(context, result.FirstError)
            : result.Value.Created ? Results.Created(CapacityReductionsV1.DetailPath(account, term, result.Value.Schedule.ScheduleId), result.Value.Schedule)
                : Results.Ok(result.Value.Schedule);
    }

    private static async Task<IResult> ReadAsync(HttpContext context, Guid account, Guid term,
        ScheduleCapacityReductionHandler handler, CancellationToken ct) =>
        CustomerSubscriptionStatusEndpoints.ReadProof(context, account, CapacityReductionsV1.Read,
            CapacityReductionsV1.BasePath(account, term)) is { } proof
            ? Respond(context, await handler.ReadAsync(proof, account, term, ct)) : Invalid(context);

    private static async Task<IResult> RecoverAsync(HttpContext context, Guid account, Guid term, Guid key,
        ScheduleCapacityReductionHandler handler, CancellationToken ct) =>
        CustomerSubscriptionStatusEndpoints.ReadProof(context, account, CapacityReductionsV1.Recover,
            CapacityReductionsV1.RecoverPath(account, term, key)) is { } proof
            ? Respond(context, await handler.RecoverAsync(proof, account, term, key, ct)) : Invalid(context);

    private static async Task<IResult> ShopsAsync(HttpContext context, Guid account, Guid term, Guid schedule,
        ScheduleCapacityReductionHandler handler, CancellationToken ct, Guid? after = null) =>
        CustomerSubscriptionStatusEndpoints.ReadProof(context, account, CapacityReductionsV1.Shops,
            CapacityReductionsV1.ShopsPath(account, term, schedule, after)) is { } proof
            ? Respond(context, await handler.ShopsAsync(proof, account, term, schedule, after, ct)) : Invalid(context);

    private static async Task<IResult> OrderAsync(HttpContext context, Guid account, Guid term, Guid schedule,
        ScheduleCapacityReductionHandler handler, CancellationToken ct)
    {
        var proof = context.Features.Get<VerifiedBillingDelegation>();
        if (proof is null || proof.AccountId != account || !CustomerBillingEndpoints.MatchesBinding(context.Request, proof,
                CheckoutOrdersV1.Create, HttpMethods.Post, CapacityReductionsV1.OrderPath(account, term, schedule)) ||
            await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<CreateReductionOrderV1>(
                context.Request, proof, CapacityReductionsV1.MaxOrderBytes, ct) is not { } request)
            return Invalid(context);
        return Respond(context, await handler.CreateOrderAsync(proof, account, term, schedule, request, ct));
    }

    private static async Task<IResult> ResolveAsync(HttpContext context, Guid account, Guid term, Guid schedule,
        ScheduleCapacityReductionHandler handler, CancellationToken ct)
    {
        var proof = context.Features.Get<VerifiedBillingDelegation>();
        if (proof is null || proof.AccountId != account || !CustomerBillingEndpoints.MatchesBinding(context.Request, proof,
                CapacityReductionsV1.Resolve, HttpMethods.Post, CapacityReductionsV1.ResolvePath(account, term, schedule)) ||
            await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<ResolveCapacityReductionV1>(
                context.Request, proof, CapacityReductionsV1.MaxResolveBytes, ct) is not { } request)
            return Invalid(context);
        return Respond(context, await handler.ResolveAsync(proof, account, term, schedule, request, ct));
    }

    private static IResult Invalid(HttpContext context) =>
        CustomerBillingEndpoints.Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);

    private static IResult Respond<T>(HttpContext context, ErrorOr<T> result) =>
        result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Ok(result.Value);
}
