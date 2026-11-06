using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IPaymentReceiptRepository : IRepository<PaymentReceipt>
{
    Task<PaymentReceipt?> GetByShopAndIdAsync(Guid shopId, Guid receiptId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentReceipt>> GetByPartyAsync(
        Guid shopId,
        PaymentPartyType partyType,
        Guid partyId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentAllocation>> GetAllocationsAsync(
        Guid shopId,
        IReadOnlyCollection<Guid> receiptIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentReversal>> GetReversalsAsync(
        Guid shopId,
        IReadOnlyCollection<Guid> receiptIds,
        CancellationToken cancellationToken = default);

    /// <summary>Σ of allocations whose receipt is not reversed, per target.</summary>
    Task<IReadOnlyDictionary<Guid, decimal>> GetActiveAllocatedByTargetAsync(
        Guid shopId,
        PaymentAllocationTargetType targetType,
        IReadOnlyCollection<Guid> targetIds,
        CancellationToken cancellationToken = default);

    Task<bool> HasActiveAllocationsAsync(
        Guid shopId,
        PaymentAllocationTargetType targetType,
        Guid targetId,
        CancellationToken cancellationToken = default);

    Task AddAllocationAsync(PaymentAllocation allocation, CancellationToken cancellationToken = default);

    Task AddReversalAsync(PaymentReversal reversal, CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks the party row, then each target row in id order, <c>FOR NO KEY UPDATE</c> inside the
    /// current transaction, with a five-second budget for every lock the transaction takes after it.
    /// The same order as a sale return (customer → sale), so the two serialize and never deadlock.
    /// </summary>
    Task<RowLockOutcome> LockPartyAndTargetsAsync(
        Guid shopId,
        PaymentPartyType partyType,
        Guid partyId,
        IReadOnlyCollection<Guid> targetIds,
        CancellationToken cancellationToken = default);
}
