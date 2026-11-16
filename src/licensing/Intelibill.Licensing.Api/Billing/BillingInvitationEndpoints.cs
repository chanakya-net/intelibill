using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using static Intelibill.Shared.Contracts.V1.BillingInvitationsV1;

namespace Intelibill.Licensing.Api.Billing;

internal static class BillingInvitationEndpoints
{
    internal static void MapBillingInvitations(this WebApplication app, AuthorizationPolicy policy)
    {
        app.MapGet(AccountRoute + "/invitations/owner", OwnerAsync).RequireAuthorization(policy);
        app.MapPost(AccountRoute + "/invitations", IssueAsync).RequireAuthorization(policy);
        app.MapGet(AccountRoute + "/members", RosterAsync).RequireAuthorization(policy);
        app.MapGet(AccountRoute + "/members/after/{after:guid}", RosterAsync).RequireAuthorization(policy);
        app.MapGet(InboxPath, InboxAsync).RequireAuthorization(policy);
        app.MapGet(InboxPath + "/after/{after:guid}", InboxAsync).RequireAuthorization(policy);
        app.MapPost(InboxPath + "/{invitationId:guid}/proof", ProofAsync).RequireAuthorization(policy);
        app.MapPost(InboxPath + "/{invitationId:guid}/accept", AcceptAsync).RequireAuthorization(policy);
    }

    private static async Task<IResult> OwnerAsync(HttpContext context, Guid accountId,
        BillingInvitationHandlers handler, CancellationToken ct) =>
        BindEmpty(context, OwnerCheck, HttpMethods.Get, AccountInvitations(accountId) + "/owner", accountId) is { } assertion
            ? Respond(context, await handler.OwnerAsync(assertion, ct)) : Invalid(context);

    private static async Task<IResult> RosterAsync(HttpContext context, Guid accountId,
        BillingInvitationHandlers handler, CancellationToken ct, Guid? after = null) =>
        BindEmpty(context, Roster, HttpMethods.Get, Members(accountId, after), accountId) is { } assertion
            ? Respond(context, await handler.RosterAsync(assertion, after, ct)) : Invalid(context);

    private static async Task<IResult> InboxAsync(HttpContext context, BillingInvitationHandlers handler,
        CancellationToken ct, Guid? after = null) =>
        BindEmpty(context, Inbox, HttpMethods.Get, InboxPage(after)) is { } assertion
            ? Respond(context, await handler.InboxAsync(assertion, after, ct)) : Invalid(context);

    private static async Task<IResult> IssueAsync(HttpContext context, Guid accountId,
        BillingInvitationHandlers handler, CancellationToken ct)
    {
        var assertion = Bind(context, Issue, HttpMethods.Post, AccountInvitations(accountId), accountId);
        if (assertion is null || await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<IssueBillingInvitationV1>(
            context.Request, assertion, MaximumRequestBytes, ct) is not { } body)
            return Invalid(context);

        return Respond(context, await handler.IssueAsync(assertion, body, ct), StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> ProofAsync(HttpContext context, Guid invitationId,
        BillingInvitationHandlers handler, CancellationToken ct) =>
        BindEmpty(context, Proof, HttpMethods.Post, Invitation(invitationId, "proof")) is { } assertion
            ? Respond(context, await handler.ProofAsync(assertion, invitationId, ct)) : Invalid(context);

    private static async Task<IResult> AcceptAsync(HttpContext context, Guid invitationId,
        BillingInvitationHandlers handler, CancellationToken ct)
    {
        var assertion = Bind(context, Accept, HttpMethods.Post, Invitation(invitationId, "accept"));
        if (assertion is null || await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<AcceptBillingInvitationV1>(
            context.Request, assertion, MaximumRequestBytes, ct) is not { } body)
            return Invalid(context);

        return Respond(context, await handler.AcceptAsync(assertion, invitationId, body, ct));
    }

    private static VerifiedBillingDelegation? Bind(HttpContext context, string action, string method, string path,
        Guid? accountId = null) => context.Features.Get<VerifiedBillingDelegation>() is { } assertion &&
        assertion.AccountId == accountId && CustomerBillingEndpoints.MatchesBinding(context.Request, assertion, action, method, path)
            ? assertion : null;

    private static VerifiedBillingDelegation? BindEmpty(HttpContext context, string action, string method, string path,
        Guid? accountId = null) =>
        Bind(context, action, method, path, accountId) is { BodySha256: MerchantBillingDelegationV1.EmptyBodySha256 } assertion &&
        context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody != true &&
        context.Request.ContentLength is null or 0 && !context.Request.Headers.ContainsKey("Transfer-Encoding")
            ? assertion : null;

    private static IResult Respond<T>(HttpContext context, ErrorOr<T> result, int status = StatusCodes.Status200OK) =>
        result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Json(result.Value, statusCode: status);

    private static IResult Invalid(HttpContext context) =>
        CustomerBillingEndpoints.Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);
}
