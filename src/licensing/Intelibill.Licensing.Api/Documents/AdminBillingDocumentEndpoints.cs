using ErrorOr;
using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Api.Bootstrap;
using Intelibill.Licensing.Api.Catalogue;
using Intelibill.Licensing.Application.Documents;
using static Intelibill.Licensing.Api.Payments.AdminManualPaymentEndpoints;

namespace Intelibill.Licensing.Api.Documents;

internal static class AdminBillingDocumentEndpoints
{
    internal static void MapAdminBillingDocuments(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/accounts/{account:guid}/orders/{order:guid}/billing-documents")
            .RequireAuthorization(ProviderSessions.PaymentPolicy);
        group.MapGet("", ReadAsync);
        group.MapPost("/preview", PreviewAsync);
        group.MapPost("", UploadAsync);
        group.MapGet("/operations/{operationKey:guid}", RecoverAsync);
    }

    private static async Task<IResult> ReadAsync(HttpContext context, Guid account, Guid order, UploadBillingDocumentHandler handler) =>
        Caller(context) is { } caller ? Respond(context, await handler.ReadAsync(caller, account, order, context.RequestAborted))
            : SessionEnded(context);

    private static async Task<IResult> PreviewAsync(HttpContext context, Guid account, Guid order, AdminSignInHttp http,
        UploadBillingDocumentHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        if (Caller(context) is not { } caller) return SessionEnded(context);
        var body = await CatalogueDraftEndpoints.ReadAsync<BillingDocumentMetadata>(context, BillingDocumentMultipart.CommandLimit);
        if (body.Refusal is not null) return body.Refusal;
        var preview = await handler.PreviewAsync(caller, account, order, body.Value, context.RequestAborted);
        return preview.IsError ? Problem(context, preview.FirstError) : Respond(context, preview);
    }

    private static async Task<IResult> UploadAsync(HttpContext context, Guid account, Guid order, AdminSignInHttp http,
        UploadBillingDocumentHandler handler)
    {
        if (await CatalogueDraftEndpoints.AcceptAsync(context, http) is { } refused) return refused;
        if (Caller(context) is not { } caller) return SessionEnded(context);
        var body = await BillingDocumentMultipart.ReadAsync(context);
        if (body.IsError) return Problem(context, body.FirstError);
        var input = body.Value;
        var result = await handler.UploadAsync(caller, account, order, input.Command, input.FileName, input.Content, CancellationToken.None);
        return result.IsError ? Problem(context, result.FirstError)
            : Respond<BillingDocumentVersionView>(context, result.Value.Document, result.Value.Created ? 201 : 200);
    }

    private static async Task<IResult> RecoverAsync(HttpContext context, Guid account, Guid order, Guid operationKey,
        UploadBillingDocumentHandler handler) => Caller(context) is { } caller
        ? Respond(context, await handler.RecoverAsync(caller, account, order, operationKey, context.RequestAborted))
        : SessionEnded(context);

    private static IResult Problem(HttpContext context, Error error) => error.Code switch
    {
        "BillingDocument.TooLarge" => BootstrapHttp.Problem(context, 413, error.Code, error.Description),
        "BillingDocument.MediaTypeUnsupported" => BootstrapHttp.Problem(context, 415, error.Code, error.Description),
        _ => CatalogueDraftEndpoints.Problem(context, error),
    };
}
