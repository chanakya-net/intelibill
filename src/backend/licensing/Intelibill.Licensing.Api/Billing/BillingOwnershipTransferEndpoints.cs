using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;

namespace Intelibill.Licensing.Api.Billing;

internal static class BillingOwnershipTransferEndpoints
{
    internal static void MapOwnershipTransfers(this WebApplication app, AuthorizationPolicy policy)
    {
        const string path = BillingOwnershipTransfersV1.Path;
        app.MapPost(BillingInvitationsV1.AccountRoute + "/ownership-transfers", IssueAsync).RequireAuthorization(policy);
        app.MapGet(path, InboxAsync).RequireAuthorization(policy);
        app.MapGet(path + "/after/{after:guid}", InboxAsync).RequireAuthorization(policy);
        app.MapGet(path + "/{transfer:guid}", PreviewAsync).RequireAuthorization(policy);
        app.MapPost(path + "/{transfer:guid}/accept", AcceptAsync).RequireAuthorization(policy);
        app.MapGet(BillingOwnershipTransfersV1.Operations + "/{operation}/{key:guid}", RecoverAsync).RequireAuthorization(policy);
    }

    private static async Task<IResult> IssueAsync(HttpContext context, Guid accountId, TransferBillingOwnershipHandler handler,
        CancellationToken ct)
    {
        var proof = Bind(context, accountId, BillingOwnershipTransfersV1.Issue, "POST", BillingOwnershipTransfersV1.IssuePath(accountId));
        if (proof is null || await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<IssueBillingOwnershipTransferV1>(
                context.Request, proof, 4096, ct) is not { } request)
            return Invalid(context);
        return Respond(context, await handler.IssueAsync(proof, accountId, request, ct), StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> InboxAsync(HttpContext context, Guid? after, TransferBillingOwnershipHandler handler,
        CancellationToken ct) =>
        Bind(context, null, BillingOwnershipTransfersV1.Inbox, "GET", BillingOwnershipTransfersV1.InboxPath(after)) is { } proof
            ? Respond(context, await handler.InboxAsync(proof, after, ct)) : Invalid(context);

    private static async Task<IResult> PreviewAsync(HttpContext context, Guid transfer, TransferBillingOwnershipHandler handler,
        CancellationToken ct) =>
        Bind(context, null, BillingOwnershipTransfersV1.Preview, "GET", BillingOwnershipTransfersV1.PreviewPath(transfer)) is { } proof
            ? Respond(context, await handler.PreviewAsync(proof, transfer, ct)) : Invalid(context);

    private static async Task<IResult> AcceptAsync(HttpContext context, Guid transfer, TransferBillingOwnershipHandler handler,
        CancellationToken ct)
    {
        var proof = Bind(context, null, BillingOwnershipTransfersV1.Accept, "POST", BillingOwnershipTransfersV1.AcceptPath(transfer));
        if (proof is null || await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<AcceptBillingOwnershipTransferV1>(
                context.Request, proof, 1024, ct) is not { } request)
            return Invalid(context);
        return Respond(context, await handler.AcceptAsync(proof, transfer, request, ct));
    }

    private static async Task<IResult> RecoverAsync(HttpContext context, string operation, Guid key,
        TransferBillingOwnershipHandler handler, CancellationToken ct)
    {
        if (!BillingOwnershipTransfersV1.IsOperation(operation) ||
            Bind(context, null, BillingOwnershipTransfersV1.Recover, "GET", BillingOwnershipTransfersV1.OperationPath(operation, key)) is not { } proof)
            return Invalid(context);

        return operation == "issue"
            ? Respond(context, await handler.RecoverIssueAsync(proof, key, ct))
            : Respond(context, await handler.RecoverAcceptAsync(proof, key, ct));
    }

    private static VerifiedBillingDelegation? Bind(HttpContext context, Guid? account, string action, string method, string path)
    {
        var request = context.Request;
        var proof = context.Features.Get<VerifiedBillingDelegation>();
        if (proof is null || proof.AccountId != account || !CustomerBillingEndpoints.MatchesBinding(request, proof, action, method, path))
            return null;
        return method == "POST" || (proof.BodySha256 == MerchantBillingDelegationV1.EmptyBodySha256 &&
            context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody != true && request.ContentLength is null or 0 &&
            !request.Headers.ContainsKey("Transfer-Encoding")) ? proof : null;
    }

    private static IResult Respond<T>(HttpContext context, ErrorOr<T> result, int status = StatusCodes.Status200OK) =>
        result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Json(result.Value, statusCode: status);

    private static IResult Invalid(HttpContext context) =>
        CustomerBillingEndpoints.Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);
}
