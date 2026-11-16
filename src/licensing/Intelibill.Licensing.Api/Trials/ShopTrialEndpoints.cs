using System.Net.Http.Headers;
using ErrorOr;
using Intelibill.Licensing.Api.Billing;
using Intelibill.Licensing.Api.Status;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Trials;
using Intelibill.Licensing.Infrastructure.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;

namespace Intelibill.Licensing.Api.Trials;

internal static class ShopTrialEndpoints
{
    internal static void MapShopTrials(this WebApplication app, AuthorizationPolicy policy)
    {
        app.MapGet(MerchantBillingDelegationV1.InternalPrefix + "/shops/{shop:guid}/trial", PreviewAsync)
            .RequireAuthorization(policy);
        app.MapPost(ShopTrialsV1.DeliveryPath, ReceiveAsync);
    }

    private static async Task<IResult> PreviewAsync(HttpContext context, Guid shop, StartTrialHandler handler, CancellationToken ct)
    {
        var proof = CustomerSubscriptionStatusEndpoints.ReadProof(context, null, ShopTrialsV1.PreviewAction, ShopTrialsV1.PreviewPath(shop));
        return proof is null || shop == Guid.Empty ? Invalid(context) : Respond(context, await handler.PreviewAsync(proof, shop, ct));
    }

    private static async Task<IResult> ReceiveAsync(HttpContext context, ITrialDeliveryVerifier verifier,
        StartTrialHandler handler, CancellationToken ct)
    {
        var request = context.Request;
        var authorization = request.Headers.Authorization;
        var rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget;
        if (request.Path != ShopTrialsV1.DeliveryPath || request.PathBase.HasValue || request.QueryString.HasValue ||
            (!string.IsNullOrEmpty(rawTarget) && rawTarget != ShopTrialsV1.DeliveryPath) ||
            authorization.Count != 1 || !AuthenticationHeaderValue.TryParse(authorization[0], out var credential) ||
            !credential.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) ||
            credential.Parameter is not { } token || verifier.VerifyTrialDelivery(token) is not { } proof ||
            await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<ShopTrialConsentV1>(request, proof.Digest,
                ShopTrialsV1.MaximumBodyBytes, ct) is not { } consent || !Valid(consent))
            return Invalid(context);

        return Respond(context, await handler.ApplyAsync(consent, proof, ct));
    }

    private static bool Valid(ShopTrialConsentV1 consent) =>
        consent.EventId != Guid.Empty && consent.ShopId != Guid.Empty && consent.ActorId != Guid.Empty &&
        consent.MembershipId != Guid.Empty && consent.AuthorityVersion >= 0 && consent.ActiveMembers > 0 &&
        consent.AcceptedAt.Offset == TimeSpan.Zero && consent.AcceptedAt > DateTimeOffset.UnixEpoch &&
        consent.AcceptedAt < DateTimeOffset.MaxValue.AddDays(-ShopTrialsV1.DurationDays) &&
        consent.AcceptedAt.UtcTicks % 10 == 0 && ShopTrialsV1.Valid(consent.Acceptance);

    private static IResult Invalid(HttpContext context) =>
        CustomerBillingEndpoints.Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);

    private static IResult Respond<T>(HttpContext context, ErrorOr<T> result) =>
        result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Ok(result.Value);
}
