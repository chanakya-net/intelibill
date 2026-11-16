using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;

namespace Intelibill.Licensing.Api.Billing;

internal static class BillingMemberEndpoints
{
    internal static void MapBillingMembers(this WebApplication app, AuthorizationPolicy policy) =>
        app.MapPost(BillingInvitationsV1.AccountRoute + "/members/{memberId:guid}/remove", RemoveAsync)
            .RequireAuthorization(policy);

    private static async Task<IResult> RemoveAsync(HttpContext context, Guid accountId, Guid memberId,
        RemoveBillingManagerHandler handler, CancellationToken ct)
    {
        var assertion = context.Features.Get<VerifiedBillingDelegation>();
        var path = BillingInvitationsV1.MemberRemoval(accountId, memberId);
        if (assertion is null || assertion.AccountId != accountId ||
            !CustomerBillingEndpoints.MatchesBinding(context.Request, assertion, BillingInvitationsV1.RemoveMember,
                HttpMethods.Post, path) ||
            await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<RemoveBillingManagerV1>(context.Request, assertion,
                BillingInvitationsV1.MaximumRemovalBodyBytes, ct) is not { } body)
            return CustomerBillingEndpoints.Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);

        var result = await handler.HandleAsync(assertion, memberId, body, ct);
        return result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Ok(result.Value);
    }
}
