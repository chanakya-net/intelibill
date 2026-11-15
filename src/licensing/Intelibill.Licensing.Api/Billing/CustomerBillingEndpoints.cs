using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Web.PublicErrors;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;

namespace Intelibill.Licensing.Api.Billing;

internal static class CustomerBillingEndpoints
{
    internal static void AddCustomerBillingAuthentication(this IServiceCollection services) =>
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, MerchantBillingDelegationAuthentication>(
            MerchantBillingDelegationAuthentication.SchemeName, _ => { });

    internal static void MapCustomerBilling(this WebApplication app)
    {
        app.UseWhen(context => context.Request.Path.StartsWithSegments(MerchantBillingDelegationV1.InternalPrefix),
            branch => branch.Use(async (context, next) =>
            {
                context.Response.OnStarting(static state =>
                {
                    ((HttpResponse)state).Headers.CacheControl = "no-store";
                    return Task.CompletedTask;
                }, context.Response);
                await next(context);
            }));
        var policy = new AuthorizationPolicyBuilder(MerchantBillingDelegationAuthentication.SchemeName)
            .RequireAuthenticatedUser().Build();
        app.MapGet(MerchantBillingDelegationV1.InternalPrefix + "/accounts/{accountId:guid}/access", GetAccessAsync)
            .RequireAuthorization(policy);
    }

    private static async Task<IResult> GetAccessAsync(
        HttpContext context, Guid accountId, IBillingDelegationAccess access, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var delegation = context.Features.Get<VerifiedBillingDelegation>();
        if (delegation is null || !MatchesRequest(request, accountId, delegation))
            return Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);

        var result = await access.AuthorizeAsync(delegation, cancellationToken);
        if (!result.IsError)
            return Results.Ok(result.Value);

        var error = result.FirstError;
        var status = error.Type switch
        {
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status503ServiceUnavailable,
        };
        return Problem(context, status, error);
    }

    private static bool MatchesRequest(HttpRequest request, Guid accountId, VerifiedBillingDelegation delegation)
    {
        var path = MerchantBillingDelegationV1.AccountAccessPath(accountId);
        var requestIds = request.Headers[MerchantBillingDelegationV1.RequestIdHeader];
        var rawTarget = request.HttpContext.Features.Get<IHttpRequestFeature>()?.RawTarget;
        return requestIds.Count == 1 && Guid.TryParseExact(requestIds[0], "D", out var requestId) &&
            requestId == delegation.RequestId && accountId == delegation.AccountId &&
            delegation.Action == MerchantBillingDelegationV1.ReadAccount &&
            delegation.Method == HttpMethods.Get && request.Method == delegation.Method &&
            delegation.Path == path && request.Path.Value == path && !request.PathBase.HasValue && !request.QueryString.HasValue &&
            (string.IsNullOrEmpty(rawTarget) || rawTarget == path) &&
            request.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody != true &&
            request.ContentLength is null or 0 && !request.Headers.ContainsKey("Transfer-Encoding") &&
            delegation.BodySha256 == MerchantBillingDelegationV1.EmptyBodySha256;
    }

    internal static IResult Problem(HttpContext context, int status, Error error) =>
        Results.Json(new PublicError(status, error.Code, error.Description, SafeTraceId.Resolve(context)),
            statusCode: status, contentType: "application/problem+json");
}
