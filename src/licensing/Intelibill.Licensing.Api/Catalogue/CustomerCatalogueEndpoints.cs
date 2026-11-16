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
        app.MapGet(PublishedCatalogueV1.Path + "/versions/{versionId:guid}", ReadVersionAsync).RequireAuthorization(policy);
    }

    private static async Task<IResult> ReadAsync(HttpContext context, ReadPublishedCatalogueHandler handler,
        CancellationToken token, Guid? after = null)
    {
        var delegation = ReadDelegation(context, PublishedCatalogueV1.ReadPath(after));
        if (delegation is null || after == Guid.Empty) return Invalid(context);

        var result = await handler.HandleAsync(delegation, after, token);
        return result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Ok(result.Value);
    }

    private static async Task<IResult> ReadVersionAsync(HttpContext context, Guid versionId, ReadPublishedCatalogueHandler handler,
        CancellationToken token)
    {
        var delegation = ReadDelegation(context, PublishedCatalogueV1.VersionPath(versionId));
        if (delegation is null || versionId == Guid.Empty) return Invalid(context);

        var result = await handler.ReadVersionAsync(delegation, versionId, token);
        return result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Ok(result.Value);
    }

    private static VerifiedBillingDelegation? ReadDelegation(HttpContext context, string path)
    {
        var request = context.Request;
        var delegation = context.Features.Get<VerifiedBillingDelegation>();
        return delegation is { AccountId: null } &&
            CustomerBillingEndpoints.MatchesBinding(request, delegation, PublishedCatalogueV1.Read, HttpMethods.Get, path) &&
            context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody != true &&
            request.ContentLength is null or 0 && !request.Headers.ContainsKey("Transfer-Encoding") &&
            delegation.BodySha256 == MerchantBillingDelegationV1.EmptyBodySha256 ? delegation : null;
    }

    private static IResult Invalid(HttpContext context) =>
        CustomerBillingEndpoints.Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);
}
