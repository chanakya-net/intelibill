using Intelibill.Licensing.Api.Billing;
using Intelibill.Licensing.Api.Status;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Documents;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;

namespace Intelibill.Licensing.Api.Documents;

internal static class CustomerBillingDocumentEndpoints
{
    internal static void MapCustomerBillingDocuments(this WebApplication app, AuthorizationPolicy policy)
    {
        var group = app.MapGroup(MerchantBillingDelegationV1.AccountsPath + "/{account:guid}/orders/{order:guid}/billing-documents")
            .RequireAuthorization(policy);
        group.MapGet("", ListAsync);
        group.MapGet("/{version:guid}/content", ContentAsync);
    }

    private static async Task<IResult> ListAsync(HttpContext context, Guid account, Guid order,
        ReadBillingDocumentHandler handler, CancellationToken ct)
    {
        var proof = CustomerSubscriptionStatusEndpoints.ReadProof(context, account, BillingDocumentsV1.List,
            BillingDocumentsV1.ListPath(account, order));
        if (proof is null) return Invalid(context);
        var result = await handler.ListAsync(proof, account, order, ct);
        return result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Ok(result.Value);
    }

    private static async Task<IResult> ContentAsync(HttpContext context, Guid account, Guid order, Guid version,
        ReadBillingDocumentHandler handler, CancellationToken ct)
    {
        var proof = CustomerSubscriptionStatusEndpoints.ReadProof(context, account, BillingDocumentsV1.Download,
            BillingDocumentsV1.ContentPath(account, order, version));
        if (proof is null) return Invalid(context);
        var result = await handler.ContentAsync(proof, account, order, version, ct);
        if (result.IsError) return CustomerBillingEndpoints.Failure(context, result.FirstError);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers[BillingDocumentsV1.DigestHeader] = result.Value.Sha256;
        return Results.File(result.Value.Bytes, BillingDocumentsV1.MediaType, BillingDocumentsV1.FileName(version));
    }

    private static IResult Invalid(HttpContext context) =>
        CustomerBillingEndpoints.Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);
}
