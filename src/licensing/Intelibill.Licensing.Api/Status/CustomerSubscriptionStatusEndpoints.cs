using ErrorOr;
using Intelibill.Licensing.Api.Billing;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Status;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;

namespace Intelibill.Licensing.Api.Status;

/// <summary>
/// Private signed status reads under the existing no-store delegation boundary. The assertion must name exactly
/// this action, method, canonical path and an empty body; the shop read is actor-scoped and carries no account.
/// </summary>
internal static class CustomerSubscriptionStatusEndpoints
{
    internal static void MapCustomerSubscriptionStatus(this WebApplication app, AuthorizationPolicy policy)
    {
        app.MapGet(MerchantBillingDelegationV1.InternalPrefix + "/shops/{shop:guid}/status", ReadShopAsync)
            .RequireAuthorization(policy);
        const string account = MerchantBillingDelegationV1.AccountsPath + "/{account:guid}/subscriptions";
        app.MapGet(account, (HttpContext context, Guid account, ReadSubscriptionStatusHandler handler, CancellationToken ct) =>
            ReadAccountAsync(context, account, null, handler, ct)).RequireAuthorization(policy);
        app.MapGet(account + "/after/{after:guid}", (HttpContext context, Guid account, Guid after,
            ReadSubscriptionStatusHandler handler, CancellationToken ct) => ReadAccountAsync(context, account, after, handler, ct))
            .RequireAuthorization(policy);
    }

    private static async Task<IResult> ReadShopAsync(HttpContext context, Guid shop, ReadSubscriptionStatusHandler handler,
        CancellationToken ct)
    {
        var proof = ReadProof(context, null, SubscriptionStatusV1.ShopRead, SubscriptionStatusV1.ShopPath(shop));
        return proof is null ? Invalid(context) : Respond(context, await handler.ReadShopAsync(proof, shop, ct));
    }

    private static async Task<IResult> ReadAccountAsync(HttpContext context, Guid account, Guid? after,
        ReadSubscriptionStatusHandler handler, CancellationToken ct)
    {
        var proof = ReadProof(context, account, SubscriptionStatusV1.AccountRead, SubscriptionStatusV1.AccountPath(account, after));
        return proof is null ? Invalid(context) : Respond(context, await handler.ReadAccountAsync(proof, account, after, ct));
    }

    private static VerifiedBillingDelegation? ReadProof(HttpContext context, Guid? account, string action, string path)
    {
        var proof = context.Features.Get<VerifiedBillingDelegation>();
        var request = context.Request;
        return proof is not null && proof.AccountId == account
            && CustomerBillingEndpoints.MatchesBinding(request, proof, action, HttpMethods.Get, path)
            && proof.BodySha256 == MerchantBillingDelegationV1.EmptyBodySha256
            && context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody != true
            && request.ContentLength is null or 0 && !request.Headers.ContainsKey("Transfer-Encoding")
            ? proof : null;
    }

    private static IResult Invalid(HttpContext context) =>
        CustomerBillingEndpoints.Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);

    private static IResult Respond<T>(HttpContext context, ErrorOr<T> result) =>
        result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Json(result.Value);
}
