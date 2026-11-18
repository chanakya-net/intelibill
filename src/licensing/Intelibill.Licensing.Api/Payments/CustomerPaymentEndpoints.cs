using ErrorOr;
using Intelibill.Licensing.Api.Billing;
using Intelibill.Licensing.Api.Status;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authorization;

namespace Intelibill.Licensing.Api.Payments;

internal static class CustomerPaymentEndpoints
{
    internal static void MapCustomerPayments(this WebApplication app, AuthorizationPolicy policy)
    {
        var group = app.MapGroup(MerchantBillingDelegationV1.AccountsPath + "/{account:guid}/orders/{order:guid}/payment")
            .RequireAuthorization(policy);
        group.MapGet("", ReadAsync);
        group.MapGet("/confirmations", ConfirmationsAsync);
        group.MapGet("/confirmations/{id:guid}/document", DocumentAsync);
        group.MapPost("/attempts", StartAsync);
        group.MapPost("/attempts/{attempt:guid}/verify", VerifyAsync);
        // Local-only controls resolve the original simulator pin, independently of the current provider selection.
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
            group.MapPost("/attempts/{attempt:guid}/simulator/capture", CaptureAsync);
    }

    private static async Task<IResult> ConfirmationsAsync(HttpContext context, Guid account, Guid order,
        ReadPaymentConfirmationHandler handler, CancellationToken ct) =>
        CustomerSubscriptionStatusEndpoints.ReadProof(context, account, CheckoutPaymentsV1.Confirmations,
            CheckoutPaymentsV1.ConfirmationsPath(account, order)) is { } proof
            ? Respond(context, await handler.ListAsync(proof, account, order, ct)) : Invalid(context);

    private static async Task<IResult> DocumentAsync(HttpContext context, Guid account, Guid order, Guid id,
        ReadPaymentConfirmationHandler handler, CancellationToken ct)
    {
        var proof = CustomerSubscriptionStatusEndpoints.ReadProof(context, account, CheckoutPaymentsV1.ConfirmationDocument,
            CheckoutPaymentsV1.ConfirmationDocumentPath(account, order, id));
        if (proof is null) return Invalid(context);
        var result = await handler.DocumentAsync(proof, account, order, id, ct);
        return result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError)
            : AdminPaymentConfirmationEndpoints.Attachment(context, result.Value, id);
    }

    private static async Task<IResult> ReadAsync(HttpContext context, Guid account, Guid order,
        ReadCheckoutPaymentHandler handler, CancellationToken ct) =>
        CustomerSubscriptionStatusEndpoints.ReadProof(context, account, CheckoutPaymentsV1.Read,
            CheckoutPaymentsV1.PaymentPath(account, order)) is { } proof
            ? Respond(context, await handler.ReadAsync(proof, account, order, ct)) : Invalid(context);

    private static async Task<IResult> StartAsync(HttpContext context, Guid account, Guid order,
        StartCheckoutPaymentHandler handler, CancellationToken ct)
    {
        var proof = PostProof(context, account, CheckoutPaymentsV1.Start, CheckoutPaymentsV1.AttemptsPath(account, order));
        if (proof is null || await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<StartCheckoutPaymentV1>(
                context.Request, proof, CheckoutPaymentsV1.MaxStartBytes, ct) is not { } request)
            return Invalid(context);
        var result = await handler.HandleAsync(proof, account, order, request, ct);
        return result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError)
            : Results.Json(result.Value.Payment, statusCode: result.Value.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
    }

    private static async Task<IResult> VerifyAsync(HttpContext context, Guid account, Guid order, Guid attempt,
        VerifyCheckoutPaymentHandler handler, CancellationToken ct)
    {
        var proof = PostProof(context, account, CheckoutPaymentsV1.Verify, CheckoutPaymentsV1.VerifyPath(account, order, attempt));
        if (proof is null || await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<VerifyCheckoutPaymentV1>(
                context.Request, proof, CheckoutPaymentsV1.MaxVerifyBytes, ct) is not { } request)
            return Invalid(context);
        return Respond(context, await handler.HandleAsync(proof, account, order, attempt, request, ct));
    }

    private static async Task<IResult> CaptureAsync(HttpContext context, Guid account, Guid order, Guid attempt,
        SimulateCheckoutCaptureHandler handler, CancellationToken ct)
    {
        var proof = PostProof(context, account, CheckoutPaymentsV1.Simulate, CheckoutPaymentsV1.CapturePath(account, order, attempt));
        if (proof is null || await CustomerBillingAccountEndpoints.ReadBoundBodyAsync<SimulateCheckoutCaptureV1>(
                context.Request, proof, CheckoutPaymentsV1.MaxVerifyBytes, ct) is not { } request)
            return Invalid(context);
        return Respond(context, await handler.HandleAsync(proof, account, order, attempt, request, ct));
    }

    private static VerifiedBillingDelegation? PostProof(HttpContext context, Guid account, string action, string path) =>
        context.Features.Get<VerifiedBillingDelegation>() is { } proof && proof.AccountId == account &&
        CustomerBillingEndpoints.MatchesBinding(context.Request, proof, action, HttpMethods.Post, path) ? proof : null;

    private static IResult Invalid(HttpContext context) =>
        CustomerBillingEndpoints.Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);

    private static IResult Respond<T>(HttpContext context, ErrorOr<T> result) =>
        result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Ok(result.Value);
}
