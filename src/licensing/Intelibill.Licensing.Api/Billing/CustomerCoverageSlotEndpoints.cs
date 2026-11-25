using Intelibill.Licensing.Application.Coverage;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using static Intelibill.Licensing.Api.Billing.ShopAffiliationEndpoints;

namespace Intelibill.Licensing.Api.Billing;

internal static class CustomerCoverageSlotEndpoints
{
    internal static void MapCoverageSlots(this WebApplication app, AuthorizationPolicy policy)
    {
        var routes = app.MapGroup(MerchantBillingDelegationV1.AccountsPath + "/{account:guid}").RequireAuthorization(policy);
        const string slots = "/subscriptions/{term:guid}/slot-assignments";
        routes.MapPost(slots, RequestAsync);
        routes.MapGet(slots, ReadAsync);
        routes.MapGet(slots + "/pages/{page:int}", ReadAsync);
        routes.MapGet(slots + "/operations/{key:guid}", RecoverAsync);
        routes.MapGet("/slot-assignments/{shop:guid}/{assignment:guid}/owner", OwnerAsync);
    }

    private static async Task<IResult> RequestAsync(HttpContext context, Guid account, Guid term, AssignUnusedCoverageSlotHandler handler, CancellationToken ct)
    {
        var proof = ReadProof(context, account, CoverageSlotsV1.Request, "POST", CoverageSlotsV1.Path(account, term));
        if (proof is null || await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<AssignUnusedCoverageSlotV1>(
                context.Request, proof, 1024, ct) is not { } command) return Invalid(context);
        return Respond(context, await handler.RequestAsync(proof, account, term, command, ct));
    }

    private static async Task<IResult> ReadAsync(HttpContext context, Guid account, Guid term, AssignUnusedCoverageSlotHandler handler, CancellationToken ct, int page = 1) =>
        ReadProof(context, account, CoverageSlotsV1.Read, "GET", CoverageSlotsV1.ReadPath(account, term, page)) is { } proof
            ? Respond(context, await handler.ReadAsync(proof, account, term, page, ct)) : Invalid(context);

    private static async Task<IResult> RecoverAsync(HttpContext context, Guid account, Guid term, Guid key, AssignUnusedCoverageSlotHandler handler, CancellationToken ct) =>
        ReadProof(context, account, CoverageSlotsV1.Recover, "GET", $"{CoverageSlotsV1.Path(account, term)}/operations/{key:D}") is { } proof
            ? Respond(context, await handler.RecoverAsync(proof, account, term, key, ct)) : Invalid(context);

    private static async Task<IResult> OwnerAsync(HttpContext context, Guid account, Guid shop, Guid assignment, AssignUnusedCoverageSlotHandler handler, CancellationToken ct) =>
        ReadProof(context, null, CoverageSlotsV1.OwnerRead, "GET", CoverageSlotsV1.OwnerPath(account, shop, assignment)) is { } proof
            ? Respond(context, await handler.OwnerAsync(proof, account, shop, assignment, ct)) : Invalid(context);
}
