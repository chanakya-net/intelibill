using Intelibill.Application.Features.Tills;
using Intelibill.Domain.ValueObjects;
using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Features.Payments.Commands.AllocatePayment;
using Intelibill.Application.Features.Payments.Commands.ReversePayment;
using Intelibill.Application.Features.Payments.DTOs;
using Intelibill.Application.Features.Payments.Queries.GetPartyBalances;
using Intelibill.Application.Features.Payments.Queries.GetPaymentReceipt;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

/// <summary>
/// Payment receipts of customers and suppliers: their detail, later allocation to invoices or
/// purchase receipts, and their one reversal. Payments themselves are recorded through
/// <c>customers/{id}/payments</c> and <c>suppliers/{id}/payments</c>.
/// </summary>
[ApiController]
[Route("api/payments")]
[Authorize]
public sealed class PaymentsController(IMessageBus bus) : AuthenticatedControllerBase(bus)
{
    [HttpGet("balances")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> GetBalances(
        [FromQuery] PaymentPartyType partyType,
        [FromQuery] Guid partyId,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<PartyBalancesDto>>(
            new GetPartyBalancesQuery(UserId!.Value, ActiveShopId!.Value, partyType, partyId),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("{receiptId:guid}")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> GetReceipt(Guid receiptId, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<PaymentReceiptDto>>(
            new GetPaymentReceiptQuery(UserId!.Value, ActiveShopId!.Value, receiptId),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("{receiptId:guid}/allocations")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> Allocate(
        Guid receiptId,
        [FromBody] AllocatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<PaymentReceiptDto>>(
            new AllocatePaymentCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                receiptId,
                request.Allocations ?? [],
                request.OperationKey),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("{receiptId:guid}/reversal")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> Reverse(
        Guid receiptId,
        [FromBody] ReversePaymentRequest request,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<PaymentReceiptDto>>(
            new ReversePaymentCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                receiptId,
                request.Reason ?? string.Empty,
                request.OperationKey ?? string.Empty, request.Till, request.SettlementEvidence),
            cancellationToken);

        return result.ToActionResult(Ok);
    }
}

/// <param name="OperationKey">Durable identity of this allocation; a retry with the same key replays it.</param>
public sealed record AllocatePaymentRequest(
    IReadOnlyList<PaymentAllocationRequest>? Allocations,
    string? OperationKey = null);

/// <param name="OperationKey">Required: a retry with the same key replays the committed reversal.</param>
public sealed record ReversePaymentRequest(string? Reason, string? OperationKey, TillPostingContext? Till = null, TillSettlementEvidence? SettlementEvidence = null);
