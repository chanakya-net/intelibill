using ErrorOr;
using Intelibill.Licensing.Api.Billing;
using Intelibill.Licensing.Api.Status;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Subscriptions;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;

namespace Intelibill.Licensing.Api.Subscriptions;

internal static class CustomerSubscriptionCancellationEndpoints
{
    internal static void MapCustomerSubscriptionCancellation(this WebApplication app, AuthorizationPolicy policy)
    {
        var routes = app.MapGroup(MerchantBillingDelegationV1.AccountsPath + "/{account:guid}/subscriptions/{term:guid}/cancellation")
            .RequireAuthorization(policy);
        routes.MapGet("", (HttpContext context, Guid account, Guid term, ScheduleCancellationHandler handler, CancellationToken ct) =>
            ReadAsync(context, account, term, null, handler, ct));
        routes.MapGet("/after/{after:guid}", ReadAsync);
        routes.MapPost("", ScheduleAsync);
        routes.MapGet("/operations/{operation:guid}", RecoverAsync);
    }

    private static async Task<IResult> ReadAsync(HttpContext context, Guid account, Guid term, Guid? after,
        ScheduleCancellationHandler handler, CancellationToken ct)
    {
        var proof = CustomerSubscriptionStatusEndpoints.ReadProof(context, account, SubscriptionCancellationsV1.Read,
            SubscriptionCancellationsV1.DetailPath(account, term, after));
        return proof is null ? Invalid(context) : Respond(context, await handler.ReadAsync(proof, account, term, after, ct));
    }

    private static async Task<IResult> ScheduleAsync(HttpContext context, Guid account, Guid term,
        ScheduleCancellationHandler handler, CancellationToken ct)
    {
        var proof = context.Features.Get<VerifiedBillingDelegation>();
        if (proof is null || proof.AccountId != account || !CustomerBillingEndpoints.MatchesBinding(context.Request,
                proof, SubscriptionCancellationsV1.Schedule, HttpMethods.Post, SubscriptionCancellationsV1.Path(account, term))
            || await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<ScheduleSubscriptionCancellationV1>(
                context.Request, proof, SubscriptionCancellationsV1.MaximumBodyBytes, ct) is not { } request)
            return Invalid(context);

        return Respond(context, await handler.ScheduleAsync(proof, account, term, request, ct));
    }

    private static async Task<IResult> RecoverAsync(HttpContext context, Guid account, Guid term, Guid operation,
        ScheduleCancellationHandler handler, CancellationToken ct)
    {
        var proof = CustomerSubscriptionStatusEndpoints.ReadProof(context, account, SubscriptionCancellationsV1.Recover,
            SubscriptionCancellationsV1.OperationPath(account, term, operation));
        return proof is null ? Invalid(context) : Respond(context, await handler.RecoverAsync(proof, account, term, operation, ct));
    }

    private static IResult Invalid(HttpContext context) =>
        CustomerBillingEndpoints.Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);

    private static IResult Respond<T>(HttpContext context, ErrorOr<T> result) =>
        result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Json(result.Value);
}
