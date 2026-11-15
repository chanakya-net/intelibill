using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;

namespace Intelibill.Licensing.Api.Billing;

/// <summary>Actor-scoped internal routes: list the actor's billing accounts and create one explicitly.</summary>
internal static class CustomerBillingAccountEndpoints
{
    private static readonly JsonSerializerOptions StrictJson = new()
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    internal static void MapCustomerBillingAccounts(this WebApplication app, AuthorizationPolicy policy)
    {
        app.MapGet(MerchantBillingDelegationV1.AccountsPath, ListAsync).RequireAuthorization(policy);
        app.MapGet(MerchantBillingDelegationV1.AccountsPath + "/after/{after:guid}", ListAsync).RequireAuthorization(policy);
        app.MapPost(MerchantBillingDelegationV1.AccountsPath, CreateAsync).RequireAuthorization(policy);
    }

    private static async Task<IResult> ListAsync(HttpContext context, ListBillingAccountsHandler handler,
        CancellationToken cancellationToken, Guid? after = null)
    {
        var request = context.Request;
        var delegation = context.Features.Get<VerifiedBillingDelegation>();
        if (delegation is null || delegation.AccountId is not null ||
            !CustomerBillingEndpoints.MatchesBinding(request, delegation, MerchantBillingDelegationV1.ListAccounts,
                HttpMethods.Get, MerchantBillingDelegationV1.AccountListPath(after)) ||
            context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true ||
            request.ContentLength is not (null or 0) || request.Headers.ContainsKey("Transfer-Encoding") ||
            delegation.BodySha256 != MerchantBillingDelegationV1.EmptyBodySha256)
            return CustomerBillingEndpoints.Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);

        var result = await handler.HandleAsync(delegation, after, cancellationToken);
        return result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Ok(result.Value);
    }

    private static async Task<IResult> CreateAsync(HttpContext context, CreateBillingAccountHandler handler,
        CancellationToken cancellationToken)
    {
        var delegation = context.Features.Get<VerifiedBillingDelegation>();
        if (delegation is null || delegation.AccountId is not null ||
            !CustomerBillingEndpoints.MatchesBinding(context.Request, delegation, MerchantBillingDelegationV1.CreateAccount,
                HttpMethods.Post, MerchantBillingDelegationV1.AccountsPath) ||
            await ReadBoundBodyAsync<CreateBillingAccountRequestV1>(context.Request, delegation,
                MerchantBillingDelegationV1.MaximumCreateBodyBytes, cancellationToken) is not { } body)
            return CustomerBillingEndpoints.Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);

        var result = await handler.HandleAsync(delegation, body, cancellationToken);
        if (result.IsError)
            return CustomerBillingEndpoints.Failure(context, result.FirstError);
        return Results.Json(result.Value.Account,
            statusCode: result.Value.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
    }

    /// <summary>
    /// Read at most the contract's body size, require the signed digest of the exact bytes received, then
    /// deserialize strictly. Anything else is a request the assertion does not describe.
    /// </summary>
    internal static async Task<T?> ReadBoundBodyAsync<T>(HttpRequest request,
        VerifiedBillingDelegation delegation, int limit, CancellationToken cancellationToken) where T : class
    {
        if (request.ContentLength is not > 0 || request.ContentLength > limit ||
            request.Headers.ContainsKey("Transfer-Encoding") ||
            !string.Equals(request.ContentType?.Split(';')[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
            return null;

        var buffer = new byte[(int)request.ContentLength.Value];
        try
        {
            await request.Body.ReadExactlyAsync(buffer, cancellationToken);
            if (await request.Body.ReadAsync(new byte[1], cancellationToken) != 0 ||
                Convert.ToHexStringLower(SHA256.HashData(buffer)) != delegation.BodySha256)
                return null;
            return JsonSerializer.Deserialize<T>(buffer, StrictJson);
        }
        catch (Exception exception) when (exception is JsonException or EndOfStreamException or BadHttpRequestException)
        {
            return null;
        }
    }
}
