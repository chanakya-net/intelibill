using Intelibill.Licensing.Api.Billing;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Restrictions;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;

namespace Intelibill.Licensing.Api.Restrictions;

internal static partial class ClosureEndpoints
{
    internal static void MapCustomerClosures(this WebApplication app, AuthorizationPolicy policy)
    {
        app.MapPost(MerchantBillingDelegationV1.InternalPrefix + "/shops/{shop:guid}/closure", CloseAsync)
            .RequireAuthorization(policy);
        MapAccountClosures(app, policy);
    }

    private static async Task<IResult> CloseAsync(HttpContext context, Guid shop, CloseShopHandler handler, CancellationToken ct)
    {
        var proof = context.Features.Get<VerifiedBillingDelegation>();
        if (proof is null || proof.AccountId is not null || !CustomerBillingEndpoints.MatchesBinding(context.Request, proof,
                ShopClosuresV1.ShopAction, HttpMethods.Post, ShopClosuresV1.ShopPath(shop)) ||
            await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<ShopClosureRequestV1>(
                context.Request, proof, ShopClosuresV1.BodyLimit, ct) is not { } input)
            return Invalid(context);
        return Respond(context, await handler.CloseAsync(proof, shop, input, ct));
    }

    private static IResult Invalid(HttpContext context) =>
        CustomerBillingEndpoints.Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);

    private static IResult Respond<T>(HttpContext context, ErrorOr.ErrorOr<T> result) =>
        result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Json(result.Value);
}
