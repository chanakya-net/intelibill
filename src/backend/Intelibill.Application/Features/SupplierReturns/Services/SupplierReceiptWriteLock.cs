using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.SupplierReturns.Services;

/// <summary>
/// The lock a command takes before it writes a receipt's stock rows on their row versions alone.
/// </summary>
/// <remarks>
/// A supplier return shipment and a receipt reversal lock the supplier row, then the receipt row,
/// and write the stock rows (item total, batch) only when they save. Receipt cost correction, batch
/// void and supplier reassignment write the stock rows first and the receipt row last. Arriving in
/// between, one of those would hold a stock row while it waits for the receipt, the other would
/// hold the receipt while it waits for that stock row, and PostgreSQL would end one of them as a
/// deadlock. Taking the supplier row first, the first lock on both sides, makes the later command
/// wait before it holds anything. Whichever goes second still fails its row version check.
/// </remarks>
internal static class SupplierReceiptWriteLock
{
    /// <summary>
    /// Locks the supplier row for the rest of the transaction. A lock held past its budget answers
    /// <c>PurchaseReceipt.Busy</c>; a supplier that is not in the shop answers
    /// <paramref name="supplierMissing"/>.
    /// </summary>
    public static async Task<ErrorOr<Success>> TakeBeforeReceiptWriteAsync(
        this ISupplierReturnMutationLock locks,
        Guid shopId,
        Guid supplierId,
        Error supplierMissing,
        CancellationToken cancellationToken) =>
        await locks.LockAsync(shopId, supplierId, [], cancellationToken) switch
        {
            RowLockOutcome.Acquired => Result.Success,
            RowLockOutcome.TimedOut => Errors.PurchaseReceipt.Busy,
            RowLockOutcome.NotFound => supplierMissing,
            var outcome => throw new InvalidOperationException($"Unknown supplier lock outcome '{outcome}'."),
        };
}
