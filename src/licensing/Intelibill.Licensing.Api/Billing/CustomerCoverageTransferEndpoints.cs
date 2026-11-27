using Intelibill.Licensing.Application.Coverage;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using static Intelibill.Licensing.Api.Billing.ShopAffiliationEndpoints;

namespace Intelibill.Licensing.Api.Billing;

/// <summary>
/// P06-07a customer routes. Every route is bound to one signed action, method, path and body; preview and create are
/// distinct actions, and each consent route names exactly one account, transfer, purpose and decision.
/// </summary>
internal static class CustomerCoverageTransferEndpoints
{
    internal static void MapCoverageTransfers(this WebApplication app, AuthorizationPolicy policy)
    {
        var routes = app.MapGroup(MerchantBillingDelegationV1.AccountsPath + "/{account:guid}").RequireAuthorization(policy);
        const string request = "/subscriptions/{term:guid}/coverage-transfers";
        const string transfer = "/coverage-transfers/{transfer:guid}";
        routes.MapPost(request + "/preview", PreviewAsync);
        routes.MapPost(request, RequestAsync);
        routes.MapGet(request + "/operations/{key:guid}", RecoverAsync);
        routes.MapGet("/coverage-transfers", ListAsync);
        routes.MapGet(transfer, ReadAsync);
        routes.MapPost(transfer + "/consents/{purpose}/{decision}", DecideAsync);
        routes.MapGet(transfer + "/consents/{purpose}/operations/{key:guid}", RecoverDecisionAsync);
        routes.MapGet("/coverage-transfer-shops/{shop:guid}/{transfer:guid}/owner", OwnerAsync);
    }

    private static async Task<IResult> PreviewAsync(HttpContext context, Guid account, Guid term,
        RequestCoverageTransferHandler handler, CancellationToken ct)
    {
        var proof = ReadProof(context, account, CoverageTransfersV1.Preview, "POST", CoverageTransfersV1.PreviewPath(account, term));
        if (proof is null || await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<PreviewCoverageTransferV1>(
                context.Request, proof, CoverageTransfersV1.MaxRequestBytes, ct) is not { } query)
            return Invalid(context);
        return Respond(context, await handler.PreviewAsync(proof, account, term, query, ct));
    }

    private static async Task<IResult> RequestAsync(HttpContext context, Guid account, Guid term,
        RequestCoverageTransferHandler handler, CancellationToken ct)
    {
        var proof = ReadProof(context, account, CoverageTransfersV1.Request, "POST", CoverageTransfersV1.RequestPath(account, term));
        if (proof is null || await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<RequestCoverageTransferV1>(
                context.Request, proof, CoverageTransfersV1.MaxRequestBytes, ct) is not { } command)
            return Invalid(context);
        return Respond(context, await handler.RequestAsync(proof, account, term, command, ct));
    }

    private static async Task<IResult> RecoverAsync(HttpContext context, Guid account, Guid term, Guid key,
        RequestCoverageTransferHandler handler, CancellationToken ct) =>
        ReadProof(context, account, CoverageTransfersV1.Recover, "GET",
            CoverageTransfersV1.RecoverPath(account, term, key)) is { } proof
            ? Respond(context, await handler.RecoverAsync(proof, account, term, key, ct))
            : Invalid(context);

    private static async Task<IResult> ListAsync(HttpContext context, Guid account,
        RequestCoverageTransferHandler handler, CancellationToken ct) =>
        ReadProof(context, account, CoverageTransfersV1.List, "GET", CoverageTransfersV1.ListPath(account)) is { } proof
            ? Respond(context, await handler.ListAsync(proof, account, ct))
            : Invalid(context);

    private static async Task<IResult> ReadAsync(HttpContext context, Guid account, Guid transfer,
        RequestCoverageTransferHandler handler, CancellationToken ct) =>
        ReadProof(context, account, CoverageTransfersV1.Read, "GET", CoverageTransfersV1.ReadPath(account, transfer)) is { } proof
            ? Respond(context, await handler.ReadAsync(proof, account, transfer, ct))
            : Invalid(context);

    private static async Task<IResult> DecideAsync(HttpContext context, Guid account, Guid transfer, string purpose,
        string decision, CoverageTransferConsentHandler handler, CancellationToken ct)
    {
        if (CoverageTransfersV1.BillingPurpose(purpose) is not { } bound || CoverageTransfersV1.Decision(decision) is not { } kind)
            return Results.NotFound();
        var proof = ReadProof(context, account, CoverageTransfersV1.Consent, "POST",
            CoverageTransfersV1.ConsentPath(account, transfer, bound, kind));
        if (proof is null || await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<CoverageTransferDecisionV1>(
                context.Request, proof, CoverageTransfersV1.MaxRequestBytes, ct) is not { } command)
            return Invalid(context);
        return Respond(context, await handler.DecideAsync(proof, account, transfer, bound, kind, command, ct));
    }

    private static async Task<IResult> RecoverDecisionAsync(HttpContext context, Guid account, Guid transfer, string purpose,
        Guid key, CoverageTransferConsentHandler handler, CancellationToken ct)
    {
        if (CoverageTransfersV1.BillingPurpose(purpose) is not { } bound) return Results.NotFound();
        return ReadProof(context, account, CoverageTransfersV1.ConsentRecover, "GET",
            CoverageTransfersV1.ConsentRecoverPath(account, transfer, bound, key)) is { } proof
            ? Respond(context, await handler.RecoverAsync(proof, account, transfer, bound, key, ct))
            : Invalid(context);
    }

    private static async Task<IResult> OwnerAsync(HttpContext context, Guid account, Guid shop, Guid transfer,
        RequestCoverageTransferHandler handler, CancellationToken ct) =>
        ReadProof(context, null, CoverageTransfersV1.OwnerRead, "GET",
            CoverageTransfersV1.OwnerPath(account, shop, transfer)) is { } proof
            ? Respond(context, await handler.OwnerAsync(proof, account, shop, transfer, ct))
            : Invalid(context);
}
