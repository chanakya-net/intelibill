using Intelibill.Licensing.Api.Billing;
using Intelibill.Licensing.Api.Status;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Restrictions;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;

namespace Intelibill.Licensing.Api.Restrictions;

internal static partial class ClosureEndpoints
{
    private static void MapAccountClosures(WebApplication app, AuthorizationPolicy policy)
    {
        var account = app.MapGroup(MerchantBillingDelegationV1.AccountsPath + "/{account:guid}/closures")
            .RequireAuthorization(policy);
        account.MapGet("", ReadAccountAsync);
        account.MapPost("", ProposeAsync);
        account.MapPost("/{proposal:guid}/confirm", ConfirmAsync);
        var shop = app.MapGroup(MerchantBillingDelegationV1.InternalPrefix + "/shops/{shop:guid}/closure")
            .RequireAuthorization(policy);
        shop.MapGet("/account/{account:guid}/{affiliation:guid}", ReadOwnerAsync);
        shop.MapPost("/consent", RecordNoticeAsync);
    }

    private static async Task<IResult> ReadAccountAsync(HttpContext context, Guid account, CloseShopHandler handler, CancellationToken ct)
    {
        var proof = CustomerSubscriptionStatusEndpoints.ReadProof(context, account, ShopClosuresV1.AccountRead,
            ShopClosuresV1.AccountPath(account));
        return proof is null ? Invalid(context) : Respond(context, await handler.ReadProposalAsync(proof, account, ct));
    }

    private static async Task<IResult> ProposeAsync(HttpContext context, Guid account, CloseShopHandler handler, CancellationToken ct)
    {
        var proof = PostedProof(context, account, ShopClosuresV1.Propose, ShopClosuresV1.AccountPath(account));
        return proof is not null && await ReadBodyAsync<ProposeAccountClosureV1>(context, proof, ct) is { } input
            ? Respond(context, await handler.ProposeAsync(proof, account, input, ct)) : Invalid(context);
    }

    private static async Task<IResult> ConfirmAsync(HttpContext context, Guid account, Guid proposal,
        CloseShopHandler handler, CancellationToken ct)
    {
        var proof = PostedProof(context, account, ShopClosuresV1.Confirm, ShopClosuresV1.ConfirmPath(account, proposal));
        return proof is not null && await ReadBodyAsync<ConfirmAccountClosureV1>(context, proof, ct) is { } input
            ? Respond(context, await handler.ConfirmAccountAsync(proof, account, proposal, input, ct)) : Invalid(context);
    }

    private static async Task<IResult> ReadOwnerAsync(HttpContext context, Guid shop, Guid account, Guid affiliation,
        CloseShopHandler handler, CancellationToken ct)
    {
        var proof = CustomerSubscriptionStatusEndpoints.ReadProof(context, null, ShopClosuresV1.OwnerProposal,
            ShopClosuresV1.OwnerPath(shop, account, affiliation));
        return proof is null ? Invalid(context) : Respond(context, await handler.ReadOwnerProposalAsync(proof, account, shop, affiliation, ct));
    }

    private static async Task<IResult> RecordNoticeAsync(HttpContext context, Guid shop, CloseShopHandler handler, CancellationToken ct)
    {
        var proof = PostedProof(context, null, ShopClosuresV1.OwnerNotice, ShopClosuresV1.NoticePath(shop));
        return proof is not null && await ReadBodyAsync<OwnerClosureNoticeV1>(context, proof, ct) is { } input
            ? Respond(context, await handler.RecordNoticeAsync(proof, shop, input, ct)) : Invalid(context);
    }

    private static VerifiedBillingDelegation? PostedProof(HttpContext context, Guid? account, string action, string path) =>
        context.Features.Get<VerifiedBillingDelegation>() is { } proof && proof.AccountId == account &&
        CustomerBillingEndpoints.MatchesBinding(context.Request, proof, action, HttpMethods.Post, path) ? proof : null;

    private static Task<T?> ReadBodyAsync<T>(HttpContext context, VerifiedBillingDelegation proof, CancellationToken ct)
        where T : class => CustomerBillingAccountEndpoints.ReadBoundBodyAsync<T>(context.Request, proof, ShopClosuresV1.BodyLimit, ct);
}
