using ErrorOr;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Payments.DTOs;
using Intelibill.Application.Features.Tills;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Payments.Services;

/// <summary>
/// Records a customer or supplier payment's receipt, plans its allocations, stages the receipt rows and
/// appends the payment's till movement. Takes no row lock and never saves.
/// </summary>
public sealed class PaymentReceiptPoster(
    IPaymentReceiptRepository paymentReceiptRepository,
    IPaymentAllocationPlanner allocationPlanner,
    ITillPostingService tillPosting,
    IBusinessClock businessClock)
{
    internal ErrorOr<PaymentReceipt> RecordReceipt(
        Guid shopId,
        PaymentPartyType partyType,
        Guid partyId,
        decimal amount,
        ReceiptTenderMethod? method,
        Guid? bankAccountId,
        string? externalReference,
        DateOnly paymentDate,
        string? notes,
        Guid ledgerEntryId,
        Guid? expenseId,
        Guid actorUserId) =>
        PaymentReceipt.Record(
            shopId,
            partyType,
            partyId,
            amount,
            method ?? ReceiptTenderMethod.UnknownRecordedMethod,
            bankAccountId,
            externalReference,
            paymentDate,
            businessClock.UtcNow,
            notes,
            ledgerEntryId,
            expenseId,
            actorUserId);

    internal Task<ErrorOr<IReadOnlyList<PaymentAllocation>>> PlanAllocationsAsync(
        PaymentReceipt receipt,
        IReadOnlyList<PaymentAllocationRequest> allocationRequests,
        Guid actorUserId,
        CancellationToken cancellationToken) =>
        allocationPlanner.PlanAsync(receipt, 0m, allocationRequests, actorUserId, cancellationToken);

    internal async Task StageReceiptAsync(
        PaymentReceipt receipt,
        IReadOnlyList<PaymentAllocation> allocations,
        CancellationToken cancellationToken)
    {
        await paymentReceiptRepository.AddAsync(receipt, cancellationToken);
        foreach (var allocation in allocations)
            await paymentReceiptRepository.AddAllocationAsync(allocation, cancellationToken);
    }

    internal async Task<ErrorOr<Success>> AppendTillMovementAsync(
        TillSession session,
        TillMovementSource source,
        decimal direction,
        PaymentReceipt receipt,
        string operationKey,
        string? notes,
        Guid actorUserId,
        CancellationToken cancellationToken) =>
        await tillPosting.AppendAsync(
            session,
            new(
                source,
                receipt.Id,
                "payment",
                [new TenderLine(receipt.Method, receipt.Amount, null, null, receipt.ExternalReference, receipt.BankAccountId)],
                direction,
                businessClock.UtcNow,
                operationKey.Trim(),
                notes),
            actorUserId,
            cancellationToken);
}
