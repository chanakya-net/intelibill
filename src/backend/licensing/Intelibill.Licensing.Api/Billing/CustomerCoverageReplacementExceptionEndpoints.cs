using Intelibill.Licensing.Application.Coverage;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using static Intelibill.Licensing.Api.Billing.ShopAffiliationEndpoints;

namespace Intelibill.Licensing.Api.Billing;

internal static class CustomerCoverageReplacementExceptionEndpoints
{
    internal static void MapCoverageReplacementExceptions(this WebApplication app, AuthorizationPolicy policy)
    {
        var routes = app.MapGroup(MerchantBillingDelegationV1.AccountsPath + "/{account:guid}").RequireAuthorization(policy);
        const string path = "/subscriptions/{term:guid}/coverage-replacement-exceptions";
        routes.MapGet("/coverage-replacement-exceptions/{shop:guid}/{exceptionId:guid}/owner", OwnerAsync);
        routes.MapPost(path, RequestAsync);
        routes.MapGet(path + "/operations/{key:guid}", RecoverAsync);
        routes.MapGet(path, ReadAsync);
        routes.MapGet(path + "/pages/{page:int}", ReadAsync);
    }

    private static async Task<IResult> ReadAsync(HttpContext context, Guid account, Guid term,
        RequestCoverageReplacementExceptionHandler handler, CancellationToken ct, int page = 1) =>
        ReadProof(context, account, CoverageReplacementExceptionsV1.Read, "GET",
            CoverageReplacementExceptionsV1.ReadPath(account, term, page)) is { } proof
            ? Respond(context, await handler.ReadAsync(proof, account, term, page, ct))
            : Invalid(context);

    private static async Task<IResult> RequestAsync(HttpContext context, Guid account, Guid term,
        RequestCoverageReplacementExceptionHandler handler, CancellationToken ct)
    {
        var proof = ReadProof(context, account, CoverageReplacementExceptionsV1.Request, "POST",
            CoverageReplacementExceptionsV1.Path(account, term));
        if (proof is null || await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<RequestCoverageReplacementExceptionV1>(
                context.Request, proof, CoverageReplacementExceptionsV1.MaxRequestBytes, ct) is not { } command)
            return Invalid(context);
        return Respond(context, await handler.RequestAsync(proof, account, term, command, ct));
    }

    private static async Task<IResult> RecoverAsync(HttpContext context, Guid account, Guid term, Guid key,
        RequestCoverageReplacementExceptionHandler handler, CancellationToken ct) =>
        ReadProof(context, account, CoverageReplacementExceptionsV1.Recover, "GET",
            CoverageReplacementExceptionsV1.RecoverPath(account, term, key)) is { } proof
            ? Respond(context, await handler.RecoverAsync(proof, account, term, key, ct))
            : Invalid(context);

    private static async Task<IResult> OwnerAsync(HttpContext context, Guid account, Guid shop, Guid exceptionId,
        RequestCoverageReplacementExceptionHandler handler, CancellationToken ct) =>
        ReadProof(context, null, CoverageReplacementExceptionsV1.OwnerRead, "GET",
            CoverageReplacementExceptionsV1.OwnerPath(account, shop, exceptionId)) is { } proof
            ? Respond(context, await handler.OwnerAsync(proof, account, shop, exceptionId, ct))
            : Invalid(context);
}
