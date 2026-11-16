using Intelibill.Licensing.Api.Billing;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Catalogue;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;

namespace Intelibill.Licensing.Api.Catalogue;

internal static class CustomerCatalogueEndpoints
{
    internal static void MapCustomerCatalogue(this WebApplication app, AuthorizationPolicy policy)
    {
        app.MapGet(PublishedCatalogueV1.Path, ReadAsync).RequireAuthorization(policy);
        app.MapGet(PublishedCatalogueV1.Path + "/after/{after:guid}", ReadAsync).RequireAuthorization(policy);
    }

    private static async Task<IResult> ReadAsync(HttpContext context, ReadPublishedCatalogueHandler handler,
        CancellationToken token, Guid? after = null)
    {
        var request = context.Request;
        var delegation = context.Features.Get<VerifiedBillingDelegation>();
        if (delegation is null || after == Guid.Empty || delegation.AccountId is not null ||
            !CustomerBillingEndpoints.MatchesBinding(request, delegation, PublishedCatalogueV1.Read, HttpMethods.Get,
                PublishedCatalogueV1.ReadPath(after)) ||
            context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true ||
            request.ContentLength is not (null or 0) || request.Headers.ContainsKey("Transfer-Encoding") ||
            delegation.BodySha256 != MerchantBillingDelegationV1.EmptyBodySha256)
            return CustomerBillingEndpoints.Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);

        var result = await handler.HandleAsync(delegation, after, token);
        return result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Ok(result.Value);
    }
}
