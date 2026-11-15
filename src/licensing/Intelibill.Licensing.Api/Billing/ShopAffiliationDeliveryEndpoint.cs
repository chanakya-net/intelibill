using System.Net.Http.Headers;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Coverage;
using Intelibill.Licensing.Infrastructure.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Http.Features;

namespace Intelibill.Licensing.Api.Billing;

internal static class ShopAffiliationDeliveryEndpoint
{
    internal static void MapAffiliationDelivery(this WebApplication app) =>
        app.MapPost(ShopAffiliationsV1.DeliveryPath, ReceiveAsync);

    private static async Task<IResult> ReceiveAsync(HttpContext context, IShopAffiliationDeliveryVerifier verifier,
        IShopAffiliationStore store, CancellationToken ct)
    {
        var request = context.Request;
        var authorization = request.Headers.Authorization;
        var rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget;
        if (request.Path != ShopAffiliationsV1.DeliveryPath || request.PathBase.HasValue || request.QueryString.HasValue ||
            (!string.IsNullOrEmpty(rawTarget) && rawTarget != ShopAffiliationsV1.DeliveryPath) ||
            authorization.Count != 1 || !AuthenticationHeaderValue.TryParse(authorization[0], out var credential) ||
            !credential.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) ||
            credential.Parameter is not { } token || verifier.VerifyDelivery(token) is not { } proof ||
            await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<ShopAffiliationDecisionV1>(request, proof.Digest,
                ShopAffiliationDeliveryV1.MaximumBodyBytes, ct) is not { } decision || !IsValid(decision))
            return CustomerBillingEndpoints.Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);

        var result = await store.ApplyAsync(decision, proof.Digest, proof.ReplayId, proof.ExpiresAt, ct);
        return result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Ok(result.Value);
    }

    private static bool IsValid(ShopAffiliationDecisionV1 item) =>
        item.EventId != Guid.Empty && item.RequestId != Guid.Empty && item.AccountId != Guid.Empty && item.ShopId != Guid.Empty &&
        item.TermId != Guid.Empty && item.ActorId != Guid.Empty && item.MembershipId != Guid.Empty && item.AuthorityVersion >= 0 &&
        item.ShopName is { Length: > 0 and <= 250 } && item.OwnerName is { Length: > 0 and <= 300 } &&
        item.AcceptedAt != default && item.AcceptedAt.Offset == TimeSpan.Zero;
}
