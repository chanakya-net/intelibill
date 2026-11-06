using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class PaymentReceiptRepository(ApplicationDbContext context)
    : RepositoryBase<PaymentReceipt>(context), IPaymentReceiptRepository
{
    private const string PaymentLockTimeout = "5s";

    public async Task<PaymentReceipt?> GetByShopAndIdAsync(Guid shopId, Guid receiptId, CancellationToken cancellationToken = default) =>
        await DbSet.FirstOrDefaultAsync(r => r.Id == receiptId && r.ShopId == shopId, cancellationToken);

    public async Task<IReadOnlyList<PaymentReceipt>> GetByPartyAsync(
        Guid shopId,
        PaymentPartyType partyType,
        Guid partyId,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .AsNoTracking()
            .Where(r => r.ShopId == shopId && r.PartyType == partyType && r.PartyId == partyId)
            .OrderByDescending(r => r.BusinessDate)
            .ThenByDescending(r => r.ReceivedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PaymentAllocation>> GetAllocationsAsync(
        Guid shopId,
        IReadOnlyCollection<Guid> receiptIds,
        CancellationToken cancellationToken = default)
    {
        if (receiptIds.Count == 0)
            return [];

        return await Context.PaymentAllocations
            .AsNoTracking()
            .Where(a => a.ShopId == shopId && receiptIds.Contains(a.ReceiptId))
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentReversal>> GetReversalsAsync(
        Guid shopId,
        IReadOnlyCollection<Guid> receiptIds,
        CancellationToken cancellationToken = default)
    {
        if (receiptIds.Count == 0)
            return [];

        return await Context.PaymentReversals
            .AsNoTracking()
            .Where(r => r.ShopId == shopId && receiptIds.Contains(r.ReceiptId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetActiveAllocatedByTargetAsync(
        Guid shopId,
        PaymentAllocationTargetType targetType,
        IReadOnlyCollection<Guid> targetIds,
        CancellationToken cancellationToken = default)
    {
        if (targetIds.Count == 0)
            return new Dictionary<Guid, decimal>();

        var rows = await ActiveAllocations(shopId, targetType)
            .Where(a => targetIds.Contains(a.TargetId))
            .GroupBy(a => a.TargetId)
            .Select(g => new { TargetId = g.Key, Amount = g.Sum(a => a.Amount) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.TargetId, r => r.Amount);
    }

    public async Task<bool> HasActiveAllocationsAsync(
        Guid shopId,
        PaymentAllocationTargetType targetType,
        Guid targetId,
        CancellationToken cancellationToken = default) =>
        await ActiveAllocations(shopId, targetType).AnyAsync(a => a.TargetId == targetId, cancellationToken);

    public async Task AddAllocationAsync(PaymentAllocation allocation, CancellationToken cancellationToken = default) =>
        await Context.PaymentAllocations.AddAsync(allocation, cancellationToken);

    public async Task AddReversalAsync(PaymentReversal reversal, CancellationToken cancellationToken = default) =>
        await Context.PaymentReversals.AddAsync(reversal, cancellationToken);

    public async Task<RowLockOutcome> LockPartyAndTargetsAsync(
        Guid shopId,
        PaymentPartyType partyType,
        Guid partyId,
        IReadOnlyCollection<Guid> targetIds,
        CancellationToken cancellationToken = default)
    {
        if (!Context.Database.IsNpgsql())
            return RowLockOutcome.Acquired;

        if (Context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Payment locks must be taken inside a transaction.");

        await Context.Database.ExecuteSqlAsync(
            $"SELECT set_config('lock_timeout', {PaymentLockTimeout}, true)",
            cancellationToken);

        var ordered = targetIds.Distinct().Order().ToArray();
        try
        {
            // Same order as a sale return: the party first, then its documents sorted by id. NO KEY
            // UPDATE, so inserts that only reference these rows do not queue behind the lock.
            var party = partyType == PaymentPartyType.Customer
                ? await Context.Database
                    .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM customers WHERE id = {partyId} AND shop_id = {shopId} FOR NO KEY UPDATE")
                    .ToListAsync(cancellationToken)
                : await Context.Database
                    .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM suppliers WHERE id = {partyId} AND shop_id = {shopId} FOR NO KEY UPDATE")
                    .ToListAsync(cancellationToken);
            if (party.Count == 0)
                return RowLockOutcome.NotFound;

            foreach (var targetId in ordered)
            {
                // A missing target is not an error here: the planner reports it with its own code.
                if (partyType == PaymentPartyType.Customer)
                {
                    _ = await Context.Database
                        .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM sales WHERE id = {targetId} AND shop_id = {shopId} FOR NO KEY UPDATE")
                        .ToListAsync(cancellationToken);
                }
                else
                {
                    _ = await Context.Database
                        .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM purchase_receipt_values WHERE id = {targetId} AND shop_id = {shopId} FOR NO KEY UPDATE")
                        .ToListAsync(cancellationToken);
                }
            }
        }
        // Npgsql counts a lock timeout as transient, so the execution strategy hands it back wrapped.
        catch (Exception ex) when ((ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            return RowLockOutcome.TimedOut;
        }

        return RowLockOutcome.Acquired;
    }

    private IQueryable<PaymentAllocation> ActiveAllocations(Guid shopId, PaymentAllocationTargetType targetType) =>
        Context.PaymentAllocations
            .AsNoTracking()
            .Where(a => a.ShopId == shopId
                && a.TargetType == targetType
                && !Context.PaymentReversals.Any(r => r.ReceiptId == a.ReceiptId));
}
