using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Coverage;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;

namespace Intelibill.Licensing.Api.Billing;

internal static class ShopAffiliationEndpoints
{
    internal static void MapShopAffiliations(this WebApplication app, AuthorizationPolicy policy)
    {
        const string path = MerchantBillingDelegationV1.AccountsPath + "/{account:guid}/affiliations";
        app.MapGet(path, ListAsync).RequireAuthorization(policy);
        app.MapPost(path, RequestAsync).RequireAuthorization(policy);
        app.MapGet(path + "/{shop:guid}/{request:guid}/owner", OwnerReadAsync).RequireAuthorization(policy);
    }

    private static async Task<IResult> ListAsync(HttpContext context, Guid account, ShopAffiliationHandlers handlers, CancellationToken ct)
    {
        var proof = ReadProof(context, account, ShopAffiliationsV1.Read, "GET", ShopAffiliationsV1.AccountPath(account));
        return proof is null ? Invalid(context) : Respond(context, await handlers.ListAsync(proof, account, ct));
    }

    private static async Task<IResult> RequestAsync(HttpContext context, Guid account, ShopAffiliationHandlers handlers, CancellationToken ct)
    {
        var proof = ReadProof(context, account, ShopAffiliationsV1.Request, "POST", ShopAffiliationsV1.AccountPath(account));
        if (proof is null || await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<RequestShopAffiliationV1>(
                context.Request, proof, 1024, ct) is not { } request)
            return Invalid(context);

        return Respond(context, await handlers.RequestAsync(proof, account, request, ct), StatusCodes.Status201Created);
    }

    private static async Task<IResult> OwnerReadAsync(HttpContext context, Guid account, Guid shop, Guid request,
        ShopAffiliationHandlers handlers, CancellationToken ct)
    {
        var proof = ReadProof(context, null, ShopAffiliationsV1.OwnerRead, "GET", ShopAffiliationsV1.OwnerPath(account, shop, request));
        return proof is null ? Invalid(context) : Respond(context, await handlers.ReadForOwnerAsync(proof, account, shop, request, ct));
    }

    private static VerifiedBillingDelegation? ReadProof(HttpContext context, Guid? account, string action, string method, string path)
    {
        var proof = context.Features.Get<VerifiedBillingDelegation>();
        var request = context.Request;
        if (proof is null || proof.AccountId != account || !CustomerBillingEndpoints.MatchesBinding(request, proof, action, method, path))
            return null;

        return method != "GET" || (proof.BodySha256 == MerchantBillingDelegationV1.EmptyBodySha256 &&
            context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody != true && request.ContentLength is null or 0 &&
            !request.Headers.ContainsKey("Transfer-Encoding")) ? proof : null;
    }

    private static IResult Invalid(HttpContext context) =>
        CustomerBillingEndpoints.Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);

    private static IResult Respond<T>(HttpContext context, ErrorOr<T> result, int status = StatusCodes.Status200OK) =>
        result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Json(result.Value, statusCode: status);
}
