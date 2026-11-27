using Intelibill.Licensing.Application.Coverage;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using static Intelibill.Licensing.Api.Billing.ShopAffiliationEndpoints;

namespace Intelibill.Licensing.Api.Billing;

internal static class CustomerCoverageReplacementEndpoints
{
    internal static void MapCoverageReplacements(this WebApplication app, AuthorizationPolicy policy)
    {
        var routes = app.MapGroup(MerchantBillingDelegationV1.AccountsPath + "/{account:guid}").RequireAuthorization(policy);
        const string path = "/subscriptions/{term:guid}/coverage-replacements";
        routes.MapGet("/coverage-replacements/{shop:guid}/{replacement:guid}/owner", OwnerAsync);
        routes.MapPost(path, ScheduleAsync);
        routes.MapGet(path + "/operations/{key:guid}", RecoverAsync);
        routes.MapGet(path, ReadAsync);
        routes.MapGet(path + "/pages/{page:int}", ReadAsync);
    }

    private static async Task<IResult> ReadAsync(HttpContext context, Guid account, Guid term,
        ScheduleCoverageReplacementHandler handler, CancellationToken ct, int page = 1) =>
        ReadProof(context, account, CoverageReplacementsV1.Read, "GET", CoverageReplacementsV1.ReadPath(account, term, page)) is { } proof
            ? Respond(context, await handler.ReadAsync(proof, account, term, page, ct)) : Invalid(context);

    private static async Task<IResult> ScheduleAsync(HttpContext context, Guid account, Guid term,
        ScheduleCoverageReplacementHandler handler, CancellationToken ct)
    {
        var proof = ReadProof(context, account, CoverageReplacementsV1.Schedule, "POST", CoverageReplacementsV1.Path(account, term));
        if (proof is null || await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<ScheduleCoverageReplacementV1>(
                context.Request, proof, 1024, ct) is not { } command) return Invalid(context);
        return Respond(context, await handler.ScheduleAsync(proof, account, term, command, ct));
    }

    private static async Task<IResult> RecoverAsync(HttpContext context, Guid account, Guid term, Guid key,
        ScheduleCoverageReplacementHandler handler, CancellationToken ct) =>
        ReadProof(context, account, CoverageReplacementsV1.Recover, "GET", $"{CoverageReplacementsV1.Path(account, term)}/operations/{key:D}") is { } proof
            ? Respond(context, await handler.RecoverAsync(proof, account, term, key, ct)) : Invalid(context);

    private static async Task<IResult> OwnerAsync(HttpContext context, Guid account, Guid shop, Guid replacement,
        ScheduleCoverageReplacementHandler handler, CancellationToken ct) =>
        ReadProof(context, null, CoverageReplacementsV1.OwnerRead, "GET", CoverageReplacementsV1.OwnerPath(account, shop, replacement)) is { } proof
            ? Respond(context, await handler.OwnerAsync(proof, account, shop, replacement, ct)) : Invalid(context);
}
