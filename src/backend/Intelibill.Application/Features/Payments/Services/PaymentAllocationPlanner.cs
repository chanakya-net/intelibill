using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Payments.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Payments.Services;

/// <summary>
/// Turns requested allocations into <see cref="PaymentAllocation"/> rows, or refuses them. Must run
/// after the party and target locks are held, so the outstanding amounts it reads stay true until
/// commit.
/// </summary>
public interface IPaymentAllocationPlanner
{
    Task<ErrorOr<IReadOnlyList<PaymentAllocation>>> PlanAsync(
        PaymentReceipt receipt,
        decimal alreadyAllocated,
        IReadOnlyList<PaymentAllocationRequest> requests,
        Guid actorUserId,
        CancellationToken cancellationToken);
}

internal sealed class PaymentAllocationPlanner(IInvoiceBalanceReader balanceReader) : IPaymentAllocationPlanner
{
    public const int MaxAllocationsPerRequest = 50;

    public async Task<ErrorOr<IReadOnlyList<PaymentAllocation>>> PlanAsync(
        PaymentReceipt receipt,
        decimal alreadyAllocated,
        IReadOnlyList<PaymentAllocationRequest> requests,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
            return Array.Empty<PaymentAllocation>();

        var shapeErrors = ValidateShape(requests);
        if (shapeErrors.Count > 0)
            return shapeErrors;

        var targetIds = requests.Select(r => r.TargetId).ToList();
        var outstanding = receipt.PartyType == PaymentPartyType.Customer
            ? (await balanceReader.GetForCustomerAsync(receipt.ShopId, receipt.PartyId, targetIds, cancellationToken))
                .ToDictionary(b => b.SaleId, b => b.OutstandingAmount)
            : (await balanceReader.GetForSupplierAsync(receipt.ShopId, receipt.PartyId, targetIds, cancellationToken))
                .ToDictionary(b => b.PurchaseReceiptValueId, b => b.OutstandingAmount);

        var errors = new List<Error>();
        foreach (var request in requests)
        {
            if (!outstanding.TryGetValue(request.TargetId, out var open))
                errors.Add(Errors.Payment.AllocationTargetNotFound(request.TargetId));
            else if (request.Amount > open)
                errors.Add(Errors.Payment.AllocationExceedsOutstanding(request.TargetId, open));
        }

        if (errors.Count > 0)
            return errors;

        var unallocated = receipt.Amount - alreadyAllocated;
        if (requests.Sum(r => r.Amount) > unallocated)
            return Errors.Payment.AllocationExceedsReceipt(Math.Max(0m, unallocated));

        var allocations = new List<PaymentAllocation>(requests.Count);
        foreach (var request in requests)
        {
            var allocation = PaymentAllocation.Create(receipt, request.TargetId, request.Amount, actorUserId);
            if (allocation.IsError)
                return allocation.Errors;
            allocations.Add(allocation.Value);
        }

        return allocations;
    }

    internal static List<Error> ValidateShape(IReadOnlyList<PaymentAllocationRequest> requests)
    {
        var errors = new List<Error>();
        if (requests.Count > MaxAllocationsPerRequest)
            errors.Add(Errors.Payment.TooManyAllocations(MaxAllocationsPerRequest));

        foreach (var request in requests)
        {
            if (request.Amount <= 0m || decimal.Round(request.Amount, 2) != request.Amount)
                errors.Add(Errors.Payment.AllocationAmountInvalid);
        }

        foreach (var duplicate in requests.GroupBy(r => r.TargetId).Where(g => g.Count() > 1))
            errors.Add(Errors.Payment.DuplicateAllocationTarget(duplicate.Key));

        return errors;
    }
}
